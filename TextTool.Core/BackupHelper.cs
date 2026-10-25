namespace TextTool.Services;

/// <summary>
/// 文件备份辅助 —— 覆盖写前备份原文件，防止误操作导致数据丢失。
/// 是否备份由调用方（`WriteMode.OverwriteWithBackup`）决定，不是覆盖的强制副作用。
/// 备份命名：`{name}.bak`（最新），历史轮换为 `{name}.bak.1` / `.bak.2` ...
/// </summary>
public static class BackupHelper
{
    /// <summary>保留的备份份数（不含最新的 .bak）</summary>
    private const int MaxHistory = 3;

    /// <summary>
    /// 为 <paramref name="filePath"/> 创建备份。
    /// 最新备份命名为 `{name}.bak`，旧备份向后轮换为 `.bak.1`/`.bak.2`。
    /// 若文件不存在或无内容，不创建备份。
    /// </summary>
    public static string? CreateBackup(string filePath)
    {
        if (!File.Exists(filePath))
            return null;
        if (new FileInfo(filePath).Length == 0)
            return null;

        string backup = GetBackupPath(filePath, 0);

        // 轮换：删除最旧的历史，逐个后移
        string oldest = GetBackupPath(filePath, MaxHistory);
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (int i = MaxHistory - 1; i >= 1; i--)
        {
            string src = GetBackupPath(filePath, i);
            if (File.Exists(src))
                File.Move(src, GetBackupPath(filePath, i + 1), overwrite: true);
        }

        // 现有的 .bak 后移为 .bak.1
        string current = GetBackupPath(filePath, 1);
        if (File.Exists(backup))
            File.Move(backup, current, overwrite: true);

        File.Copy(filePath, backup);
        return backup;
    }

    /// <summary>获取第 <paramref name="index"/> 份备份路径（0 = 最新 .bak）。</summary>
    private static string GetBackupPath(string filePath, int index)
    {
        string ext = index == 0
            ? ".bak"
            : $".bak.{index}";
        return filePath + ext;
    }
}
