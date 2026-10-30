namespace TextTool.Tests.Scripts;

/// <summary>
/// Linux 包的权限位回归：scripts/linux-zip.py 必须给启动器写 0755、其余 0644。
///
/// 这一条是「解压即用」的前提。Compress-Archive 不写 Unix 权限，条目全成 0644，
/// 用户拿到包第一件事就是 Permission denied——所以判据不看脚本源码，看产出的 zip 本身。
/// </summary>
public class LinuxZipTests
{
    private static string ScriptPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "linux-zip.py"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), ScriptPath);
    }

    [Fact]
    public void 启动器写0755_其余0644()
    {
        if (Python() is null)
            return;   // 没有 python 的环境判不了这条，交给装了解释器的机器与 CI

        string root = Path.Combine(Path.GetTempPath(), "tt-zip-" + Guid.NewGuid().ToString("N"));
        string src = Path.Combine(root, "publish");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "texttool"), "#!/usr/bin/env bash\nexit 0\n");
        File.WriteAllText(Path.Combine(src, "texttool.dll"), "x");
        string zip = Path.Combine(root, "out.zip");

        try
        {
            var (code, stderr) = Run(Python()!, ScriptPath, src, zip, "texttool");
            Assert.True(code == 0, stderr);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            // 按无符号右移取高 16 位：ExternalAttributes 是有符号 int，直接右移会带进符号位。
            var modes = archive.Entries.ToDictionary(e => e.FullName, e => (int)((uint)e.ExternalAttributes >> 16));
            Assert.Equal(0x81ED, modes["texttool"]);        // -rwxr-xr-x
            Assert.Equal(0x81A4, modes["texttool.dll"]);    // -rw-r--r--
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响断言。
            }
        }
    }

    private static string? Python()
    {
        foreach (var name in new[] { "python3", "python" })
        {
            foreach (var segment in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(segment))
                    continue;
                string candidate = Path.Combine(segment.Trim('"'), name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    private static (int Code, string Stderr) Run(string exe, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 python");
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stderr);
    }
}
