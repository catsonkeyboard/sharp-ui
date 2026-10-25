using SharpUI;

namespace SharpUI.Tests;

// IdResolver.LastDuplicateKeys 为进程级静态;Assign 的写入方(IdResolverTests/PerfTests/
// FocusTests/InteractionTests)必须同集串行,否则并行枚举会撞 "Collection was modified"。
[Collection("interaction")]
public class IdResolverTests
{
    static PanelElement Build(bool swapped = false)
    {
        ButtonElement inc = new("+", () => { }); inc.Key = "inc";
        ButtonElement dec = new("-", () => { }); dec.Key = "dec";
        PanelElement col = Ui.Panel();
        col.Add(swapped ? dec : inc);
        col.Add(swapped ? inc : dec);
        return col;
    }

    [Fact]
    public void Assign_IsDeterministicAcrossIdenticalRebuilds()
    {
        PanelElement a = Build();
        PanelElement b = Build();
        IdResolver.Assign(a);
        IdResolver.Assign(b);
        Assert.Equal(a.Children![0].ResolvedId, b.Children![0].ResolvedId);
        Assert.Equal(a.Children[1].ResolvedId, b.Children[1].ResolvedId);
    }

    [Fact]
    public void KeyedChild_KeepsIdWhenReordered()
    {
        PanelElement a = Build();
        PanelElement b = Build(swapped: true);
        IdResolver.Assign(a);
        IdResolver.Assign(b);
        // a 的第一个孩子与 b 的第二个孩子是同一个语义按钮(键 "inc")
        Assert.Equal(a.Children![0].ResolvedId, b.Children![1].ResolvedId);
    }

    [Fact]
    public void UnkeyedChildren_FallBackToTypeNameAndIndex()
    {
        PanelElement col = Ui.Column(Ui.Text("a"), Ui.Text("b"));
        IdResolver.Assign(col);
        Assert.Equal("/TextElement0", col.Children![0].ResolvedId);
        Assert.Equal("/TextElement1", col.Children[1].ResolvedId);
        Assert.Equal("", col.ResolvedId);
    }

    [Fact]
    public void NestedIds_AreParentPrefixed()
    {
        PanelElement root = Ui.Column(Ui.Column(Ui.Text("x").Key("t")));
        IdResolver.Assign(root);
        Assert.StartsWith(root.Children![0].ResolvedId + "/",
            root.Children[0].Children![0].ResolvedId);
        Assert.EndsWith("/t", root.Children[0].Children![0].ResolvedId);
    }

    [Fact]
    public void SameKeyInDifferentBranches_StaysDistinct()
    {
        // 注:任务书原测试体将两个同键元素放进了同一个 Row(D10 公式下同键兄弟本就同 id,
        // 属重复键用户错误),与测试名"不同分支"矛盾;此处按测试名修正为分属两个分支。
        TextElement left = Ui.Text("l").Key("t");
        TextElement right = Ui.Text("r").Key("t");
        PanelElement root = Ui.Column(Ui.Column(left), Ui.Column(right));
        IdResolver.Assign(root);
        Assert.NotEqual(left.ResolvedId, right.ResolvedId);
    }

    [Fact]
    public void DuplicateSiblingKeys_AreDetected()
    {
        PanelElement col = Ui.Panel();
        col.Add(Ui.Text("a").Key("dup"));
        col.Add(Ui.Text("b").Key("dup"));
        col.Add(Ui.Text("c").Key("ok"));
        IdResolver.Assign(col);
        Assert.Contains(IdResolver.LastDuplicateKeys, id => id.EndsWith("/dup"));
        Assert.Equal(1, IdResolver.LastDuplicateKeys.Count);   // 第二次出现记一次
    }

    [Fact]
    public void SameKeyInDifferentBranches_NotFlagged()
    {
        PanelElement root = Ui.Column(
            Ui.Column(Ui.Text("l").Key("t")),
            Ui.Column(Ui.Text("r").Key("t")));
        IdResolver.Assign(root);
        Assert.Empty(IdResolver.LastDuplicateKeys);
    }
}
