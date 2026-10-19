using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Lexi;

/// <summary>The DPAPI social credential slot: round trip, private-directory ACL, stale temporary
/// cleanup and link rejection. Windows only; other platforms report a skip.</summary>
internal static class WindowsSecretStoreTests
{
    internal static void Run(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
        {
            check(true, "非 Windows 平台跳过 DPAPI 社交凭据检查");
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "lexi-secret-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "credentials");
        var outside = Path.Combine(root, "outside");
        string? junction = null;
        try
        {
            var store = new WindowsSocialSecretStore(root);
            check(store.Get() is null, "缺少凭据时读取为 null，且不创建目录");
            check(!Directory.Exists(directory), "缺失状态下不会顺手创建凭据目录");
            store.Set("synthetic-refresh-token");
            check(store.Get() == "synthetic-refresh-token", "DPAPI 以当前用户身份往返凭据");
            var security = new DirectoryInfo(directory).GetAccessControl();
            check(security.AreAccessRulesProtected, "凭据目录不再继承父目录权限");
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            var current = WindowsIdentity.GetCurrent().User;
            check(owner is not null && current is not null && owner.Equals(current), "凭据目录属于当前用户");

            var stale = Path.Combine(directory, ".social-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(stale, new byte[] { 1, 2, 3 });
            store.Set("second-refresh-token");
            check(!File.Exists(stale), "中断写入遗留的临时凭据文件被清理");
            check(store.Get() == "second-refresh-token", "替换后的凭据仍可读回");
            store.Delete();
            check(store.Get() is null, "删除后凭据槽为空");

            var constructorStale = Path.Combine(directory, ".social-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(constructorStale, new byte[] { 4, 5, 6 });
            var reopened = new WindowsSocialSecretStore(root);
            check(!File.Exists(constructorStale), "初始化已有凭据目录时清理崩溃遗留临时文件");

            var deleteStale = Path.Combine(directory, ".social-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(deleteStale, new byte[] { 7, 8, 9 });
            reopened.Delete();
            check(!File.Exists(deleteStale) && reopened.Get() is null, "删除入口在凭据不存在时仍清理遗留临时文件");

            var matching = Path.Combine(directory, ".social-" + Guid.NewGuid().ToString("N") + ".tmp");
            var lookalike = Path.Combine(directory, ".social-deadbeef.tmp");
            var nestedDirectory = Path.Combine(directory, "nested");
            Directory.CreateDirectory(nestedDirectory);
            var nested = Path.Combine(nestedDirectory, ".social-" + Guid.NewGuid().ToString("N") + ".tmp");
            var outsideStale = Path.Combine(root, ".social-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(matching, new byte[] { 10 });
            File.WriteAllBytes(lookalike, new byte[] { 11 });
            File.WriteAllBytes(nested, new byte[] { 12 });
            File.WriteAllBytes(outsideStale, new byte[] { 13 });
            reopened.Delete();
            check(!File.Exists(matching) && File.Exists(lookalike) && File.Exists(nested) && File.Exists(outsideStale),
                "临时文件清理只处理直接子目录中的严格生成名");
            check(reopened.Get() is not null || !File.Exists(Path.Combine(directory, "social-dpapi")),
                "路径边界清理没有创建或破坏凭据槽");

            var blocked = Path.Combine(directory, ".social-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(blocked, new byte[] { 14 });
            using (var held = new FileStream(blocked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    reopened.Delete();
                    check(false, "临时文件清理失败时必须显式失败");
                }
                catch (IOException) { check(true, "临时文件清理失败时显式失败"); }
                catch (UnauthorizedAccessException) { check(true, "临时文件清理失败时显式失败"); }
            }
            reopened.Delete();
            check(!File.Exists(blocked), "释放阻塞句柄后删除入口可完成清理");

            // A link swapped in under the credential path must never be followed.
            Directory.CreateDirectory(outside);
            var linkedRoot = Path.Combine(root, "junction-root");
            junction = Path.Combine(linkedRoot, "credentials");
            Directory.CreateDirectory(linkedRoot);
            var linked = new WindowsSocialSecretStore(linkedRoot);
            if (TryCreateJunction(junction, outside))
            {
                try { _ = linked.Get(); check(false, "链接目录下的凭据读取必须被拒绝"); }
                catch (IOException) { check(true, "链接形式的凭据目录被拒绝"); }
                try { linked.Set("linked-token"); check(false, "链接目录下的凭据写入必须被拒绝"); }
                catch (IOException) { check(true, "链接形式的凭据目录拒绝写入"); }
                try { linked.Delete(); check(false, "链接目录下的凭据删除必须被拒绝"); }
                catch (IOException) { check(true, "链接形式的凭据目录拒绝删除"); }
                try { _ = new WindowsSocialSecretStore(linkedRoot); check(false, "链接目录下的初始化必须被拒绝"); }
                catch (IOException) { check(true, "链接形式的凭据目录拒绝初始化清理"); }
                check(!File.Exists(Path.Combine(outside, "social-dpapi")), "链接目标目录没有写入任何凭据");
            }
        }
        finally
        {
            if (junction is not null && Directory.Exists(junction))
            {
                try { Directory.Delete(junction); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe",
                "/c mklink /J \"" + link + "\" \"" + target + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (process is null) return false;
            process.WaitForExit(15000);
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
