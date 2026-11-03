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

    // ---- 审计修复波 2C(I-3):同父同 Key 兄弟自动消歧后缀,状态不再串影 ----

    [Fact]
    public void DuplicateSiblingKeys_AutoDisambiguatedWithSuffix()
    {
        TextFieldElement first = new() { Key = "edit" };
        TextFieldElement second = new() { Key = "edit" };
        PanelElement col = Ui.Panel();
        col.Add(first);
        col.Add(second);
        IdResolver.Assign(col);
        Assert.Equal("/edit", first.ResolvedId);     // 首个出现无后缀(既有 id 语义保持)
        Assert.Equal("/edit#2", second.ResolvedId);  // 第二次出现追加 #2
    }

    [Fact]
    public void DuplicateSiblingKeys_ThirdOccurrence_GetsHash3()
    {
        PanelElement col = Ui.Panel();
        for (int i = 0; i < 3; i++)
            col.Add(Ui.Text(i.ToString()).Key("t"));
        IdResolver.Assign(col);
        Assert.Equal("/t", col.Children![0].ResolvedId);
        Assert.Equal("/t#2", col.Children[1].ResolvedId);
        Assert.Equal("/t#3", col.Children[2].ResolvedId);
        // LastDuplicateKeys 保留:报告基础 id(无后缀),第二/三次出现各记一次
        Assert.Equal(new[] { "/t", "/t" }, IdResolver.LastDuplicateKeys);
    }

    [Fact]
    public void DuplicateSiblingKeys_TextFieldStatesAreIndependent()
    {
        // I-3 串影实形:修复前两 TextField 共用 "tf:/edit" 同一 StateStore 条目,输入互相覆盖
        App.State = new StateStore();
        try
        {
            TextFieldElement a = new() { Key = "edit" };
            TextFieldElement b = new() { Key = "edit" };
            PanelElement col = Ui.Panel();
            col.Add(a);
            col.Add(b);
            IdResolver.Assign(col);
            a.Feed(null, new[] { 'h', 'i' });
            Assert.Equal("hi", a.CurrentText);
            Assert.Equal("", b.CurrentText);   // b 不再共享 a 的状态
        }
        finally { App.State = null; }
    }

    [Fact]
    public void DuplicateKeyCounts_ResetPerAssign_SameFrameSemantics()
    {
        // 树每帧重建:计数随 Assign 重置,第二帧仍是 /dup 与 /dup#2(不累计为 #3)
        for (int frame = 0; frame < 2; frame++)
        {
            PanelElement col = Ui.Panel();
            col.Add(Ui.Text("a").Key("dup"));
            col.Add(Ui.Text("b").Key("dup"));
            IdResolver.Assign(col);
            Assert.Equal("/dup", col.Children![0].ResolvedId);
            Assert.Equal("/dup#2", col.Children[1].ResolvedId);
        }
    }

    [Fact]
    public void AssignChildren_DuplicateKeys_AlsoDisambiguated()
    {
        // VirtualList/PopupHost 的 Arrange/Push 期路径(IdResolver.AssignChildren)与 Assign 共用消歧
        PanelElement parent = Ui.Panel();
        parent.Add(Ui.Text("a").Key("row"));
        parent.Add(Ui.Text("b").Key("row"));
        parent.ResolvedId = "";   // 模拟 AssignChildren 直连(保自 id 已就位)
        IdResolver.AssignChildren(parent);
        Assert.Equal("/row", parent.Children![0].ResolvedId);
        Assert.Equal("/row#2", parent.Children[1].ResolvedId);
    }

    // ---- 审计修复波 2C(I-2):跨窗 id 作用域(CurrentScope 前缀) ----

    [Fact]
    public void WindowScope_PrefixesAllIdsInTree()
    {
        IdResolver.CurrentScope = "7";
        try
        {
            PanelElement root = Ui.Column(Ui.Text("x").Key("k"), Ui.Text("y"));
            IdResolver.Assign(root);
            Assert.Equal("7:", root.ResolvedId);                 // 根 = scope+":"
            Assert.Equal("7:/k", root.Children![0].ResolvedId);  // 子树逐级携带前缀
            Assert.Equal("7:/TextElement1", root.Children[1].ResolvedId);
        }
        finally { IdResolver.CurrentScope = null; }
    }

    [Fact]
    public void WindowScope_SameShapeTrees_DistinctIds_NullScopeUnprefixed()
    {
        IdResolver.CurrentScope = "1";
        PanelElement treeA = Ui.Column(Ui.Scroll());
        IdResolver.Assign(treeA);
        IdResolver.CurrentScope = "2";
        PanelElement treeB = Ui.Column(Ui.Scroll());
        IdResolver.Assign(treeB);
        IdResolver.CurrentScope = null;
        try
        {
            Assert.Equal("1:/ScrollElement0", treeA.Children![0].ResolvedId);
            Assert.Equal("2:/ScrollElement0", treeB.Children![0].ResolvedId);
            Assert.NotEqual(treeA.Children[0].ResolvedId, treeB.Children[0].ResolvedId);
            // 清空后回到无前缀语义(单窗/测试兼容)
            PanelElement treeC = Ui.Column(Ui.Scroll());
            IdResolver.Assign(treeC);
            Assert.Equal("/ScrollElement0", treeC.Children![0].ResolvedId);
        }
        finally { IdResolver.CurrentScope = null; }
    }

    [Fact]
    public void WindowScope_TwoWindowsSameTree_ScrollStatesIndependent()
    {
        // I-2 串扰实形:修复前两窗同构 Scroll 的 "scroll:"+id 同键,窗 A 滚动带动窗 B
        App.State = new StateStore();
        IdResolver.CurrentScope = "1";
        ScrollElement a = TallScroll();
        LayoutEngine.Arrange(a, new RectF(0, 0, 100, 100));
        IdResolver.Assign(a);
        IdResolver.CurrentScope = "2";
        ScrollElement b = TallScroll();
        LayoutEngine.Arrange(b, new RectF(0, 0, 100, 100));
        IdResolver.Assign(b);
        try
        {
            Assert.NotEqual(a.ResolvedId, b.ResolvedId);
            a.ScrollBy(40f);
            Assert.Equal(40f, a.StateOffset());
            Assert.Equal(0f, b.StateOffset());   // 窗 B 不受窗 A 滚动影响
        }
        finally { IdResolver.CurrentScope = null; App.State = null; }
    }

    static ScrollElement TallScroll()
    {
        ScrollElement s = Ui.Scroll();
        for (int i = 0; i < 30; i++)
            s.Add(new Spacer(new SizeF(100, 20)));
        return s;
    }
}
