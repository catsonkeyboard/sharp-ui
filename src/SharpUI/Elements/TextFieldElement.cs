namespace SharpUI;

/// <summary>单行文本框(D22):编辑语义全在 TextEditing;本类只做状态绑定/度量/绘制。状态键 tf:+id。</summary>
public sealed class TextFieldElement : PanelElement, ITextFeedable
{
    readonly string _placeholder;
    public const float PadX = 8f;

    public TextFieldElement(string placeholder = "")
    {
        _placeholder = placeholder;
        Focusable = true;
        IsHitTarget = true;
        CornerRadius = 6f;
        Background = Color32.FromRgb(0x101014);
        Style.Padding = new Thickness(horizontal: PadX, vertical: 6f);
    }

    internal TextState State => App.State is { } st
        ? st.Get("tf:" + ResolvedId, new TextState())   // Get<T>() 带 new() 约束,record 全默认参主构造不满足
        : new TextState();

    internal void SetStateForTest(TextState s) => App.State!.Set("tf:" + ResolvedId, s);
    internal string CurrentText => State.Text;

    public void Feed(KeyEventData[]? keys, char[]? chars)
    {
        if (App.State is not { } st) return;
        st.Set("tf:" + ResolvedId, TextEditing.Apply(State, keys, chars));
    }

    protected internal override SizeF MeasureSelf(SizeF available)
    {
        if (App.Atlas is null) return new SizeF(80f, 28f);
        string t = State.Text.Length > 0 ? State.Text : _placeholder;
        SizeF m = App.Atlas.Measure(t, 14f);
        return new SizeF(m.Width + PadX * 2f, m.Height + 12f);
    }

    public override void Draw(in PaintContext pc)
    {
        if (Frame.IsEmpty) { base.Draw(pc); return; }
        bool focused = pc.Input.FocusedId == ResolvedId;
        Background = focused ? Color32.FromRgb(0x16161D) : Color32.FromRgb(0x101014);
        RectF parentClip = pc.Mesh.CurrentClip;
        pc.Mesh.SetClip(Frame.Intersect(parentClip));
        var solidUv = new PointF(pc.WhiteUv.X + 0.0001f, pc.WhiteUv.Y + 0.0001f);   // 退化 UV = 纯色
        if (focused)
        {
            // 聚焦描边(实现指令①):先画外圈 0x60A5FA 全帧圆角矩形,再画内缩 1px 底色(半径-1),留 1px 蓝边
            Tessellator.AddRoundedRect(pc.Mesh, Frame, CornerRadius, Color32.FromRgb(0x60A5FA), solidUv);
            Tessellator.AddRoundedRect(pc.Mesh, Frame.Deflate(new Thickness(1f)), CornerRadius - 1f, Background, solidUv);
        }
        else
        {
            Tessellator.AddRoundedRect(pc.Mesh, Frame, CornerRadius, Background, solidUv);
        }
        // Selection 公开不夹紧(裁定#25):本边界先夹状态再取选区/切片
        TextState raw = State;
        var st = new TextState(
            raw.Text,
            Math.Clamp(raw.Cursor, 0, raw.Text.Length),
            raw.Anchor < 0 ? -1 : Math.Clamp(raw.Anchor, 0, raw.Text.Length));
        string text = st.Text.Length > 0 ? st.Text : _placeholder;
        Color32 textColor = st.Text.Length > 0 ? Color32.White : Color32.FromRgb(0x666B78);   // 实现指令④:占位灰、正文白
        if (text.Length > 0 && App.Atlas is { } atlas)
        {
            (int selStart, int selLen) = TextEditing.Selection(st);
            if (selLen > 0 && st.Text.Length > 0)
            {
                float before = atlas.Measure(st.Text[..selStart], 14f).Width;
                float selW = atlas.Measure(st.Text.Substring(selStart, selLen), 14f).Width;
                pc.Mesh.AddQuad(
                    new RectF(Frame.X + PadX + before, Frame.Y + 4f, selW, Frame.Height - 8f),
                    pc.WhiteUv, Color32.FromRgb(0x3B82F6).WithAlpha(96));   // 选区覆盖层先于文本
            }
            float textTop = Frame.Y + (Frame.Height - atlas.Measure(text, 14f).Height) / 2f;
            atlas.DrawRun(pc.Mesh, text, new PointF(Frame.X + PadX, textTop), 14f, false, textColor);
            if (focused && Anim.Blink())   // 光标闪烁(D23):墙钟取模,无状态
            {
                float cursorX = Frame.X + PadX + atlas.Measure(st.Text[..st.Cursor], 14f).Width;
                pc.Mesh.AddQuad(new RectF(cursorX, Frame.Y + 4f, 1f, Frame.Height - 8f),
                    pc.WhiteUv, Color32.White);   // 光标 1×行高,仅聚焦
            }
        }
        if (Children is not null)
            foreach (Element child in Children)
                child.Draw(in pc);
        pc.Mesh.SetClip(parentClip);
    }
}
