using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AvasRoutingTool.Controls
{
    /// <summary>
    /// A numbered step heading of the Composer rail: the number until the step is done, then a check (shape and text, not colour alone).
    /// Its look is <c>Style.StepHeader</c> in <c>Resources/Styles/Composer.xaml</c>. It is announced as text with a heading level, never a tab stop.
    /// </summary>
    public class StepHeader : Control
    {
        public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
            nameof(Number), typeof(int), typeof(StepHeader), new FrameworkPropertyMetadata(1));

        public static readonly DependencyProperty IsDoneProperty = DependencyProperty.Register(
            nameof(IsDone), typeof(bool), typeof(StepHeader), new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
            nameof(IsCurrent), typeof(bool), typeof(StepHeader), new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(StepHeader), new FrameworkPropertyMetadata(string.Empty));

        public StepHeader()
        {
            Focusable = false;
            IsTabStop = false;
        }

        public int Number
        {
            get => (int)GetValue(NumberProperty);
            set => SetValue(NumberProperty, value);
        }

        public bool IsDone
        {
            get => (bool)GetValue(IsDoneProperty);
            set => SetValue(IsDoneProperty, value);
        }

        /// <summary>The first step still to do: the marker gets the accent outline and the title the primary text colour.</summary>
        public bool IsCurrent
        {
            get => (bool)GetValue(IsCurrentProperty);
            set => SetValue(IsCurrentProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new StepHeaderAutomationPeer(this);

        private sealed class StepHeaderAutomationPeer : FrameworkElementAutomationPeer
        {
            public StepHeaderAutomationPeer(StepHeader owner) : base(owner) { }

            protected override string GetClassNameCore() => nameof(StepHeader);

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

            protected override string GetNameCore()
            {
                var name = base.GetNameCore();
                return string.IsNullOrEmpty(name) ? ((StepHeader)Owner).Title : name;
            }

            protected override bool IsControlElementCore() => true;
        }
    }
}
