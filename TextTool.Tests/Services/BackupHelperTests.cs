namespace TextTool.Tests.Services;

public class BackupHelperTests
{
    private static string MakeTempFile(string content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void CreateBackup_FirstTime_CreatesBakFile()
    {
        string path = MakeTempFile("original");
        try
        {
            var backup = BackupHelper.CreateBackup(path);

            Assert.NotNull(backup);
            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal("original", File.ReadAllText(path + ".bak"));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void CreateBackup_Twice_RotatesHistory()
    {
        string path = MakeTempFile("v1");
        try
        {
            BackupHelper.CreateBackup(path);
            File.WriteAllText(path, "v2");
            BackupHelper.CreateBackup(path);

            Assert.Equal("v2", File.ReadAllText(path + ".bak"));
            Assert.Equal("v1", File.ReadAllText(path + ".bak.1"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
            File.Delete(path + ".bak.1");
        }
    }

    [Fact]
    public void CreateBackup_MultipleTimes_CapsHistory()
    {
        string path = MakeTempFile("v0");
        try
        {
            // 创建 6 份历史，验证只保留 .bak + .bak.1..3
            for (int i = 1; i <= 6; i++)
            {
                File.WriteAllText(path, $"v{i}");
                BackupHelper.CreateBackup(path);
            }

            Assert.Equal("v6", File.ReadAllText(path + ".bak"));
            Assert.Equal("v5", File.ReadAllText(path + ".bak.1"));
            Assert.Equal("v4", File.ReadAllText(path + ".bak.2"));
            Assert.Equal("v3", File.ReadAllText(path + ".bak.3"));
            Assert.False(File.Exists(path + ".bak.4")); // 超出上限被删除
        }
        finally
        {
            File.Delete(path);
            for (int i = 0; i <= 4; i++)
                File.Delete(path + (i == 0 ? ".bak" : $".bak.{i}"));
        }
    }

    [Fact]
    public void CreateBackup_MissingFile_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), "missing_" + Guid.NewGuid().ToString("N"));
        var backup = BackupHelper.CreateBackup(path);

        Assert.Null(backup);
    }

    [Fact]
    public void CreateBackup_EmptyFile_ReturnsNull()
    {
        string path = Path.GetTempFileName();
        try
        {
            var backup = BackupHelper.CreateBackup(path);
            Assert.Null(backup);
        }
        finally { File.Delete(path); }
    }
}
