namespace TextTool.Services;

/// <summary>
/// 编辑窗重建一条规则时的唯一入口。
///
/// 界面只开放一部分字段（标题、模式、档位、严重度、次数、说明），其余字段——词面表、
/// 密度门限、折叠阈值、打断权、段末字数、默认不勾选——必须原样带回去。
/// 从空白规则开始逐项赋值时，没写到的字段就等于删了：词面表丢了词面只剩正则，
/// 密度规则丢了门限就变成「每出现一次就报」，而两种都拦不住——校验只看得到剩下的东西。
///
/// 所以这里从整条复制起步，表单只盖它开放的那几项。往规则上新增字段时，
/// 只要复制跟着走，就不必再记得在编辑窗里补一行。
/// </summary>
public static class LintRuleEdit
{
    /// <summary>
    /// 以原规则为底、交给表单覆盖后返回。回调只改界面上看得到的字段；
    /// 它没碰的一律保持原样。
    /// </summary>
    public static LintRule Rebuild(LintRule original, Action<LintRule> applyForm)
    {
        var edited = original.Clone();
        applyForm(edited);
        return edited;
    }
}
