using System.Text;

namespace TextTool.Tests.Services;

public class TextUtilsTests
{
    // ================================================================
    //  StringBuilder.EndsWithAny
    // ================================================================

    [Fact]
    public void StringBuilder_EndsWithAny_Match_ReturnsTrue()
    {
        var sb = new StringBuilder("hello.");
        var set = new HashSet<char> { '.', '!' };
        Assert.True(sb.EndsWithAny(set));
    }

    [Fact]
    public void StringBuilder_EndsWithAny_NoMatch_ReturnsFalse()
    {
        var sb = new StringBuilder("hello");
        var set = new HashSet<char> { '.', '!' };
        Assert.False(sb.EndsWithAny(set));
    }

    [Fact]
    public void StringBuilder_EndsWithAny_NullSet_ReturnsFalse()
    {
        var sb = new StringBuilder("hello.");
        Assert.False(sb.EndsWithAny(null));
    }

    [Fact]
    public void StringBuilder_EndsWithAny_EmptySet_ReturnsFalse()
    {
        var sb = new StringBuilder("hello.");
        Assert.False(sb.EndsWithAny(new HashSet<char>()));
    }

    [Fact]
    public void StringBuilder_EndsWithAny_SkipsTrailingWhitespace()
    {
        var sb = new StringBuilder("hello.   ");
        var set = new HashSet<char> { '.' };
        Assert.True(sb.EndsWithAny(set));
    }

    [Fact]
    public void StringBuilder_EndsWithAny_AllWhitespace_ReturnsFalse()
    {
        var sb = new StringBuilder("   ");
        var set = new HashSet<char> { '.' };
        Assert.False(sb.EndsWithAny(set));
    }

    [Fact]
    public void StringBuilder_EndsWithAny_Empty_ReturnsFalse()
    {
        var sb = new StringBuilder("");
        var set = new HashSet<char> { '.' };
        Assert.False(sb.EndsWithAny(set));
    }

    // ================================================================
    //  String.EndsWithAny
    // ================================================================

    [Fact]
    public void String_EndsWithAny_Match_ReturnsTrue()
    {
        Assert.True("hello。".EndsWithAny(new HashSet<char> { '。', '！' }));
    }

    [Fact]
    public void String_EndsWithAny_NoMatch_ReturnsFalse()
    {
        Assert.False("hello".EndsWithAny(new HashSet<char> { '。', '！' }));
    }

    [Fact]
    public void String_EndsWithAny_NullSet_ReturnsFalse()
    {
        Assert.False("hello.".EndsWithAny(null));
    }

    [Fact]
    public void String_EndsWithAny_EmptySet_ReturnsFalse()
    {
        Assert.False("hello.".EndsWithAny(new HashSet<char>()));
    }

    [Fact]
    public void String_EndsWithAny_SkipsTrailingWhitespace()
    {
        Assert.True("hello.   ".EndsWithAny(new HashSet<char> { '.' }));
    }
}
