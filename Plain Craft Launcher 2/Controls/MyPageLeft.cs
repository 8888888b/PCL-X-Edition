using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL;

public class MyPageLeft : Grid
{
    /// <summary>
    ///     控件在 XAML 中声明的常态不透明度。
    ///     逐个进入动画会先把不透明度压到 0，结束时再恢复；如果一律恢复成固定值，
    ///     卡片标题、页面大标题这类本该完全不透明的文本就会被压成半透明（看起来发灰）。
    /// </summary>
    private static readonly ConditionalWeakTable<FrameworkElement, StrongBox<double>> BaseOpacity = new();

    private static double GetBaseOpacity(FrameworkElement element)
    {
        if (BaseOpacity.TryGetValue(element, out var cached))
            return cached.Value;
        // 首次遇到时读到的就是 XAML 里声明的值（例如分组小标题的 0.6）
        var value = element.Opacity;
        if (value <= 0.05d)
            value = 1d; // 声明为 0 的控件一般靠别的动画显示出来，不参与逐个进入
        BaseOpacity.Add(element, new StrongBox<double>(value));
        return value;
    }

    public static DependencyProperty AnimatedControlProperty =
        DependencyProperty.Register("AnimatedControl", typeof(FrameworkElement), typeof(MyPageLeft));

    private readonly int uuid = ModBase.GetUuid();

    private bool _animatedControlNullWarned;

    // 执行逐个进入动画的控件
    public FrameworkElement AnimatedControl
    {
        get
        {
            var res = GetValue(AnimatedControlProperty);
            if (res is null && !_animatedControlNullWarned)
            {
                _animatedControlNullWarned = true;
                ModBase.Log($"[MyPageLeft] 获取到 AnimatedControl(来自 {Name}) 的值为 null", ModBase.LogLevel.Debug);
            }

            return (FrameworkElement)res;
        }
        set => SetValue(AnimatedControlProperty, value);
    }

    public void TriggerShowAnimation()
    {
        if (AnimatedControl is null)
        {
            // 缩放动画
            if (RenderTransform is not ScaleTransform)
            {
                RenderTransform = new ScaleTransform(0.96d, 0.96d);
                RenderTransformOrigin = new Point(0.5d, 0.5d);
            }

            Opacity = 0d;
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaScaleTransform(this, 1d - ((ScaleTransform)RenderTransform).ScaleX,
                        ease: new ModAnimation.AniEaseOutBack((ModAnimation.AniEasePower)2)),
                    ModAnimation.AaOpacity(this, 1d, 100)
                }, "PageLeft PageChange " + uuid);
        }
        else
        {
            // 逐个进入动画
            var aniList = new List<ModAnimation.AniData>();
            var id = 0;
            var delay = 0;
            foreach (var ElementRaw in GetAllAnimControls(true))
            {
                var element = MyVirtualizingElement.TryInit(ElementRaw);
                if (element.Visibility == Visibility.Collapsed)
                {
                    // 还原之前的隐藏动画可能导致的改变（#2436）
                    element.Opacity = 1d;
                    element.RenderTransform = new TranslateTransform(0d, 0d);
                    if (element is MyListItem)
                        ((MyListItem)element).isMouseOverAnimationEnabled = true;
                }
                else
                {
                    var targetOpacity = GetBaseOpacity(element);
                    element.Opacity = 0d;
                    element.RenderTransform = new TranslateTransform(-25, 0d);
                    if (element is MyListItem)
                        ((MyListItem)element).isMouseOverAnimationEnabled = false;
                    aniList.Add(ModAnimation.AaOpacity(element, targetOpacity, 100, delay,
                        new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
                    aniList.Add(ModAnimation.AaTranslateX(element, 5d, 200, delay,
                        new ModAnimation.AniEaseOutFluent()));
                    aniList.Add(ModAnimation.AaTranslateX(element, 20d, 300, delay,
                        new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)));
                    if (element is MyListItem)
                        aniList.Add(ModAnimation.AaCode(() =>
                        {
                            var item = (MyListItem)element;
                            item.isMouseOverAnimationEnabled = true;
                            // 逐个进入动画用的是 TranslateTransform，而 RefreshColor 的颜色/缩放动画需要 ScaleTransform。
                            // 这里必须等位移动画结束后换回 ScaleTransform，否则 RefreshColor 里的强制转换会抛异常，
                            // 导致整段左栏动画中断（表现为切换页面时列表“跳”到位）。
                            if (element.RenderTransform is not ScaleTransform)
                                element.RenderTransform = new ScaleTransform(1d, 1d);
                            item.RefreshColor(this, new EventArgs());
                        }, delay + 320));
                    delay += Math.Max(15 - id, 7) * 2;
                    id += 1;
                }
            }

            ModAnimation.AniStart(aniList, "PageLeft PageChange " + uuid);
        }
    }

    public void TriggerHideAnimation()
    {
        if (AnimatedControl is null)
        {
            // 缩放动画
            if (RenderTransform is not ScaleTransform)
            {
                RenderTransform = new ScaleTransform(1d, 1d);
                RenderTransformOrigin = new Point(0.5d, 0.5d);
            }

            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaScaleTransform(this, 0.95d - ((ScaleTransform)RenderTransform).ScaleX, 110,
                        ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaOpacity(this, -Opacity, 80, 30)
                }, "PageLeft PageChange " + uuid);
        }
        else
        {
            // 逐个退出动画
            var aniList = new List<ModAnimation.AniData>();
            var id = 0;
            var controls = GetAllAnimControls();
            foreach (var Element in controls)
            {
                aniList.Add(ModAnimation.AaOpacity(Element, -Element.Opacity, 50,
                    (int)Math.Round(70d / controls.Count * id)));
                aniList.Add(ModAnimation.AaTranslateX(Element, -6, 50, (int)Math.Round(70d / controls.Count * id)));
                id += 1;
            }

            ModAnimation.AniStart(aniList, "PageLeft PageChange " + uuid);
        }
    }

    // 遍历获取所有需要生成动画的控件
    private List<FrameworkElement> GetAllAnimControls(bool ignoreInvisibility = false)
    {
        var allControls = new List<FrameworkElement>();
        GetAllAnimControls(AnimatedControl, ref allControls, ignoreInvisibility);
        return allControls;
    }

    private void GetAllAnimControls(FrameworkElement element, ref List<FrameworkElement> allControls,
        bool ignoreInvisibility)
    {
        if (!ignoreInvisibility && element.Visibility == Visibility.Collapsed)
            return;
        if (element is MyTextButton)
            allControls.Add(element);
        else if (element is MyListItem)
            allControls.Add(element);
        else if (element is ContentControl)
            GetAllAnimControls((FrameworkElement)((ContentControl)element).Content, ref allControls,
                ignoreInvisibility);
        else if (element is Panel)
            foreach (FrameworkElement Element2 in ((Panel)element).Children)
                GetAllAnimControls(Element2, ref allControls, ignoreInvisibility);
        else
            allControls.Add(element);
    }
}

public interface IRefreshable
{
    void Refresh();
}