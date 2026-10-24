using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchEngine {
    private const string ExpectedInputSha256 = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
    private const string ReleaseVersion = "2.99.1";
    private const string ProductName = "Plain Craft Launcher (PCL) 本地修改版 2.99.1";
    private const string RepositoryUrl = "https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified";
    private const string UpstreamRepositoryUrl = "https://github.com/Meloong-Git/PCL";

    internal static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) {
        foreach (var type in types) {
            yield return type;
            foreach (var child in AllTypes(type.NestedTypes)) yield return child;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // Compare meaning rather than metadata tokens or byte offsets, which change when rewriting a PE file.
    private static string Fingerprint(MethodDefinition method) {
        if (!method.HasBody) return "no-body";
        var body = method.Body;
        var indexes = body.Instructions.Select((item, index) => (item, index)).ToDictionary(x => x.item, x => x.index);
        string Index(Instruction? instruction) => instruction is null ? "end" : indexes[instruction].ToString();
        string Operand(object? value) => value switch {
            null => "",
            Instruction target => Index(target),
            Instruction[] targets => string.Join(",", targets.Select(Index)),
            MemberReference member => member.FullName,
            VariableDefinition variable => "v" + variable.Index,
            ParameterDefinition parameter => "p" + parameter.Index,
            string text => "string-sha256:" + Hash(Encoding.UTF8.GetBytes(text)),
            IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        var lines = new List<string> { "init-locals:" + body.InitLocals };
        lines.AddRange(body.Variables.Select(v => v.VariableType.FullName));
        lines.AddRange(body.Instructions.Select(i => i.OpCode.Name + " " + Operand(i.Operand)));
        lines.AddRange(body.ExceptionHandlers.Select(e => $"{e.HandlerType}:{Index(e.TryStart)}:{Index(e.TryEnd)}:{Index(e.HandlerStart)}:{Index(e.HandlerEnd)}:{Index(e.FilterStart)}:{e.CatchType?.FullName}"));
        return Hash(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    private static Dictionary<string, string> MethodHashes(AssemblyDefinition assembly) =>
        AllTypes(assembly.MainModule.Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Fingerprint);

    private static Dictionary<string, string> ResourceHashes(AssemblyDefinition assembly) =>
        assembly.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r => r.Name, r => Hash(r.GetResourceData()));

    internal static void Apply(string input, string output, string reportPath) {
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to overwrite the input executable.");
        var inputHash = Hash(File.ReadAllBytes(input));
        if (inputHash != ExpectedInputSha256) throw new InvalidOperationException("Unsupported input hash. Expected the verified official PCL 2.99.0.0 release.");

        using var resolver = new EmbeddedAssemblyResolver(input);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
        var module = assembly.MainModule;
        var originalMethods = MethodHashes(assembly);
        var originalResources = ResourceHashes(assembly);
        var types = AllTypes(module.Types).ToList();
        var methods = types.SelectMany(t => t.Methods).ToList();
        var changed = new Dictionary<string, string>();
        TypeDefinition Type(string name) => types.Single(t => t.FullName == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        MethodReference ExistingCall(string type, string name) => methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>()
            .First(m => m.DeclaringType.FullName == type && m.Name == name);
        void Mark(MethodDefinition method, string reason) => changed[method.FullName] = reason;
        void ReplaceVoid(MethodDefinition method, string reason) {
            if (method.ReturnType.MetadataType != MetadataType.Void) throw new InvalidOperationException("Expected void method: " + method.FullName);
            method.Body = new MethodBody(method);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            Mark(method, reason);
        }

        // Restore just the automatic Minecraft installation page. Keep modpack and instance modification policies unchanged.
        var loader = Method("PCL.PageDownloadInstall", "LoaderInit");
        var instructions = loader.Body.Instructions;
        var disabledStart = instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "关闭下载");
        var disabledEnd = instructions.Single(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference mr && mr.DeclaringType.Name == "LoaderTask`2");
        var startIndex = instructions.IndexOf(disabledStart);
        var endIndex = instructions.IndexOf(disabledEnd);
        var clientList = Type("PCL.ModDownload").Fields.Single(f => f.Name == "DlClientListLoader");
        disabledStart.OpCode = OpCodes.Ldsfld;
        disabledStart.Operand = clientList;
        for (var index = endIndex; index > startIndex; index--) instructions.RemoveAt(index);
        Mark(loader, "Restore the existing DlClientListLoader instead of the disabled-download error loader.");

        // Capture all compiler-generated updater workers before replacing their entry methods.
        var updaterRoots = new[] { "UpdateCheckByButton", "UpdateStart", "UpdateRestart", "UpdateReplace" }
            .Select(name => Method("PCL.ModSecret", name)).ToList();
        var updaterWorkers = new HashSet<MethodDefinition>();
        var pending = new Queue<MethodDefinition>(updaterRoots);
        while (pending.TryDequeue(out var current)) {
            if (!current.HasBody) continue;
            foreach (var referenced in current.Body.Instructions.Select(i => i.Operand).OfType<MethodDefinition>()) {
                if (referenced.DeclaringType.FullName.StartsWith("PCL.ModSecret/", StringComparison.Ordinal)
                    && referenced.Name.StartsWith("_Lambda$", StringComparison.Ordinal) && updaterWorkers.Add(referenced))
                    pending.Enqueue(referenced);
            }
        }
        foreach (var worker in updaterWorkers) {
            if (worker.ReturnType.MetadataType == MetadataType.Void) {
                ReplaceVoid(worker, "Remove the updater's generated download, patching, replacement, and restart worker.");
            } else {
                worker.Body = new MethodBody(worker) { InitLocals = true };
                var defaultResult = new VariableDefinition(worker.ReturnType);
                worker.Body.Variables.Add(defaultResult);
                worker.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, defaultResult));
                worker.Body.Instructions.Add(Instruction.Create(OpCodes.Initobj, worker.ReturnType));
                worker.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, defaultResult));
                worker.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                Mark(worker, "Remove the updater's generated worker and return an inert default value.");
            }
        }
        foreach (var root in updaterRoots) ReplaceVoid(root, "Remove launcher self-update implementation.");
        var hint = Method("PCL.ModMain", "Hint");
        var manualUpdate = updaterRoots.Single(m => m.Name == "UpdateCheckByButton");
        manualUpdate.Body.Instructions.Clear();
        manualUpdate.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "此本地修改版已移除启动器自动更新、更新下载及文件替换功能。"));
        manualUpdate.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        manualUpdate.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        manualUpdate.Body.Instructions.Add(Instruction.Create(OpCodes.Call, hint));
        manualUpdate.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        // Preserve server configuration and ordinary announcements. Skip update announcements only.
        var server = Method("PCL.ModSecret", "ServerSub");
        var serverIl = server.Body.GetILProcessor();
        var isUpdateKey = server.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "isUpdate");
        var isUpdateStore = server.Body.Instructions.Skip(server.Body.Instructions.IndexOf(isUpdateKey)).First(i => i.OpCode == OpCodes.Stloc_S);
        var updateVariable = (VariableDefinition)isUpdateStore.Operand;
        var updateSetting = server.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "SystemSystemUpdate");
        var skipNoticeBranch = server.Body.Instructions.Skip(server.Body.Instructions.IndexOf(updateSetting)).First(i => i.OpCode == OpCodes.Bgt);
        var skipNotice = (Instruction)skipNoticeBranch.Operand;
        serverIl.InsertAfter(isUpdateStore, Instruction.Create(OpCodes.Brtrue, skipNotice));
        serverIl.InsertAfter(isUpdateStore, Instruction.Create(OpCodes.Ldloc, updateVariable));
        // Redirect update actions too, even if a server announcement is not marked isUpdate.
        var startCall = server.Body.Instructions.Single(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == "PCL.ModSecret" && mr.Name == "UpdateStart");
        var actionDone = startCall.Next;
        foreach (var key in server.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr && ((string)i.Operand is "update" or "slientupdate")).ToArray()) {
            var branch = server.Body.Instructions.Skip(server.Body.Instructions.IndexOf(key)).First(i => i.OpCode.FlowControl == FlowControl.Cond_Branch);
            // The action-dispatch comparisons use brfalse; the later boolean comparison uses ceq and is ignored.
            if (branch.OpCode == OpCodes.Brfalse || branch.OpCode == OpCodes.Brfalse_S) branch.Operand = actionDone;
        }
        Mark(server, "Skip self-update announcements and update actions; retain server config and other announcement behavior.");

        // The settings control cannot re-enable code which has been removed.
        var pageReload = Method("PCL.PageSetupSystem", "Reload");
        var reloadIl = pageReload.Body.GetILProcessor();
        var first = pageReload.Body.Instructions[0];
        var settingsSet = Method("PCL.Settings", "Set");
        foreach (var ins in new[] {
            Instruction.Create(OpCodes.Ldstr, "SystemSystemUpdate"), Instruction.Create(OpCodes.Ldc_I4_3),
            Instruction.Create(OpCodes.Box, module.TypeSystem.Int32), Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Call, settingsSet)
        }) reloadIl.InsertBefore(first, ins);
        var originalRet = pageReload.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        foreach (var ins in new[] {
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, Method("PCL.PageSetupSystem", "get_ComboSystemUpdate")),
            Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Callvirt, ExistingCall("System.Windows.UIElement", "set_IsEnabled"))
        }) reloadIl.InsertBefore(originalRet, ins);
        Mark(pageReload, "Lock only the launcher-update setting to disabled; retain all other setting refresh behavior.");
        ReplaceVoid(Method("PCL.PageSetupSystem", "ComboSystemUpdate_SelectionChanged"), "Remove the update-only selection warning for the disabled updater.");

        // Identify the third-party build while retaining author credits and all embedded UI resources.
        var about = Method("PCL.PageOtherAbout", "PageOtherAbout_Loaded");
        var aboutIl = about.Body.GetILProcessor();
        var versionLabel = about.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "正式版 2.99.0.0");
        versionLabel.Operand = "本地修改版 2.99.1";
        var aboutInfoSetter = about.Body.Instructions.Single(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == "PCL.MyListItem" && mr.Name == "set_Info");
        var concatStrings = module.ImportReference(typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })!);
        aboutIl.InsertBefore(aboutInfoSetter, Instruction.Create(OpCodes.Ldstr, $" | PCL 原作者：龙腾猫跃 | 本地修改：chadqian2012-sudo | 修改版仓库：{RepositoryUrl} | 上游原仓库：{UpstreamRepositoryUrl}"));
        aboutIl.InsertBefore(aboutInfoSetter, Instruction.Create(OpCodes.Call, concatStrings));
        var aboutFirst = about.Body.Instructions[0];
        foreach (var ins in new[] {
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, Method("PCL.PageOtherAbout", "get_ItemAboutPcl")),
            Instruction.Create(OpCodes.Ldstr, ProductName), Instruction.Create(OpCodes.Callvirt, ExistingCall("PCL.MyListItem", "set_Title"))
        }) aboutIl.InsertBefore(aboutFirst, ins);
        Mark(about, "Identify release 2.99.1 and display both the third-party repository and original upstream repository URLs on the existing about-page product row.");
        foreach (var attribute in assembly.CustomAttributes.Where(a => a.AttributeType.FullName is "System.Reflection.AssemblyTitleAttribute" or "System.Reflection.AssemblyProductAttribute"))
            attribute.ConstructorArguments[0] = new CustomAttributeArgument(module.TypeSystem.String, ProductName);

        // Adding IL can make an existing short branch exceed its signed-byte range.
        foreach (var method in methods.Where(m => changed.ContainsKey(m.FullName) && m.HasBody)) {
            foreach (var instruction in method.Body.Instructions) {
                instruction.OpCode = instruction.OpCode.Code switch {
                    Code.Br_S => OpCodes.Br, Code.Brtrue_S => OpCodes.Brtrue, Code.Brfalse_S => OpCodes.Brfalse,
                    Code.Beq_S => OpCodes.Beq, Code.Bge_S => OpCodes.Bge, Code.Bge_Un_S => OpCodes.Bge_Un,
                    Code.Bgt_S => OpCodes.Bgt, Code.Bgt_Un_S => OpCodes.Bgt_Un, Code.Ble_S => OpCodes.Ble,
                    Code.Ble_Un_S => OpCodes.Ble_Un, Code.Blt_S => OpCodes.Blt, Code.Blt_Un_S => OpCodes.Blt_Un,
                    Code.Bne_Un_S => OpCodes.Bne_Un, Code.Leave_S => OpCodes.Leave, _ => instruction.OpCode
                };
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        assembly.Write(output);
        using var result = AssemblyDefinition.ReadAssembly(output);
        var actualMethods = MethodHashes(result);
        var actualResources = ResourceHashes(result);
        if (originalMethods.Count != actualMethods.Count || originalMethods.Keys.Except(actualMethods.Keys).Any())
            throw new InvalidOperationException("Unexpected method table change.");
        var actualChanges = actualMethods.Where(pair => pair.Value != originalMethods[pair.Key]).Select(pair => pair.Key).ToArray();
        if (actualChanges.Except(changed.Keys).Any() || changed.Keys.Except(actualChanges).Any())
            throw new InvalidOperationException("Unexpected method body changes: " + string.Join("; ", actualChanges.Except(changed.Keys)));
        if (originalResources.Count != actualResources.Count || originalResources.Any(pair => !actualResources.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
            throw new InvalidOperationException("An embedded resource changed.");
        // Require empty implementation bodies after re-reading the output; this is independent of the UI setting.
        foreach (var name in new[] { "UpdateStart", "UpdateRestart", "UpdateReplace" }) {
            var method = AllTypes(result.MainModule.Types).Single(t => t.FullName == "PCL.ModSecret").Methods.Single(m => m.Name == name);
            if (method.Body.Instructions.Count != 1 || method.Body.Instructions[0].OpCode != OpCodes.Ret)
                throw new InvalidOperationException("Updater implementation survived: " + name);
        }
        var report = new {
            inputSha256 = inputHash, outputSha256 = Hash(File.ReadAllBytes(output)),
            baseVersion = result.Name.Version.ToString(), releaseVersion = ReleaseVersion, product = ProductName,
            totalMethods = originalMethods.Count, changedMethods = actualChanges.Length,
            unchangedMethods = originalMethods.Count - actualChanges.Length,
            embeddedResources = originalResources.Count, allEmbeddedResourcesUnchanged = true,
            modifications = actualChanges.Select(name => new { method = name, reason = changed[name], beforeSha256 = originalMethods[name], afterSha256 = actualMethods[name] }),
            resources = actualResources
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"Created {output}");
        Console.WriteLine($"Verified {actualChanges.Length} intended changed methods, {originalMethods.Count - actualChanges.Length} unchanged methods, {originalResources.Count} unchanged embedded resources.");
    }
}
