namespace SharpUI;

/// <summary>交互控件模式样例(T2):按下+松开同元素/聚焦+Space/Enter 激活(经 IActivatable 统一管道)。
/// 不捕获拖拽(CapturesDrag=false 默认 —— 点击语义,非拖拽)。</summary>
public sealed class ButtonElement : PanelElement, IActivatable
{
    readonly Action _onClick;

    public ButtonElement(string label, Action onClick)
    {
        _onClick = onClick;
        Add(new TextElement(label).Size(16));
        Style.MainAlign = MainAxisAlignment.Center;
        Style.CrossAlign = CrossAxisAlignment.Center;
        Style.Padding = new Thickness(horizontal: 16f, vertical: 8f);
        CornerRadius = 6f;
        IsHitTarget = true;
        Focusable = true;
    }

    public void InvokeClick() => _onClick();

    /// <summary>IActivatable:按钮无禁用态,恒可激活。</summary>
    public bool CanActivate => true;

    public override void Draw(in PaintContext pc)
    {
        string? id = ResolvedId;
        if (pc.Input.FocusedId == id)
        {
            Background = Color32.FromRgb(0x60A5FA);
        }
        else
        {
            // 悬停过渡(D23):0↔1 补间只影响常态↔悬停蓝;按压/聚焦分支保持原优先级
            float h = Anim.Get("hover:" + id, pc.Input.HoveredId == id ? 1f : 0f, 0.12f);
            Background = pc.Input.HoveredId == id && pc.Input.LeftDown
                ? Color32.FromRgb(0x1D4ED8)
                : Color32.Lerp(Color32.FromRgb(0x0C63E4), Color32.FromRgb(0x3B82F6), h);
        }
        base.Draw(pc);
    }
}
