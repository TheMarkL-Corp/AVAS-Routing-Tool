using System.Linq;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The rule engine behind the UI v2 guards, on small XAML snippets (so a rule that stops detecting turns red here).</summary>
    public class UiV2GuardScannerTests
    {
        private static UiV2Allowlist Empty => UiV2Allowlist.FromJson("{}");

        private static string[] Rules(string xaml, UiV2Allowlist? allow = null) =>
            UiV2GuardScanner.Scan("Views/Sample.xaml", xaml, allow ?? Empty).Select(v => v.Rule).Distinct().OrderBy(r => r).ToArray();

        [Fact]
        public void CleanMarkup_HasNoViolations() =>
            Assert.Empty(Rules(@"<Grid MinHeight=""{StaticResource Size.Touch.Min}"" FontSize=""{StaticResource Font.Size.Body}"">
                <Grid.ColumnDefinitions><ColumnDefinition Width=""*"" /><ColumnDefinition Width=""2*"" /><ColumnDefinition Width=""Auto"" /></Grid.ColumnDefinitions>
                <TextBlock Style=""{StaticResource Style.Text.Body}"" Text=""Routed to"" Margin=""12,4"" />
                <Image Source=""{Binding PreviewBitmap, Mode=OneWay}"" Width=""{Binding ActualWidth}"" />
            </Grid>"));

        [Fact]
        public void AControlTemplate_IsFlagged() =>
            Assert.Equal(new[] { "ControlTemplate" }, Rules("<Button><Button.Template><ControlTemplate TargetType=\"Button\"><Border /></ControlTemplate></Button.Template></Button>"));

        [Fact]
        public void AKeyedStyle_IsFlagged_AnInlineStyleWithBasedOnIsNot() =>
            Assert.Equal(new[] { "KeyedStyle" }, Rules("<Window.Resources><Style x:Key=\"Foo\" TargetType=\"Button\" BasedOn=\"{StaticResource Style.Button.Primary}\" /></Window.Resources>"));

        [Fact]
        public void AnInlineStyleWithoutBasedOn_IsFlagged_UnlessItIsAllowlisted()
        {
            const string xaml = "<TextBlock><TextBlock.Style><Style TargetType=\"TextBlock\"><Setter Property=\"Opacity\" Value=\"0.5\" /></Style></TextBlock.Style></TextBlock>";
            Assert.Equal(new[] { "InlineStyleWithoutBasedOn" }, Rules(xaml));

            var allow = UiV2Allowlist.FromJson("{\"inlineStyleExceptions\":[{\"file\":\"Views/Sample.xaml\",\"contains\":\"<Style TargetType=\\\"TextBlock\\\"\",\"reason\":\"test\"}]}");
            Assert.Empty(Rules(xaml, allow));
            Assert.Equal(new[] { "InlineStyleWithoutBasedOn" }, Rules(xaml, UiV2Allowlist.FromJson("{\"inlineStyleExceptions\":[{\"file\":\"Views/Other.xaml\",\"contains\":\"<Style\",\"reason\":\"test\"}]}")));
        }

        [Theory]
        [InlineData("<Border Width=\"48\" />")]
        [InlineData("<Border Height=\"10.5\" />")]
        [InlineData("<Border MinWidth=\"320\" />")]
        [InlineData("<Border MinHeight=\"48\" />")]
        [InlineData("<Border MaxWidth=\"650\" />")]
        [InlineData("<Border MaxHeight=\"100\" />")]
        [InlineData("<ColumnDefinition Width=\"170\" />")]
        [InlineData("<RowDefinition Height=\"56\" />")]
        [InlineData("<Style TargetType=\"Button\" BasedOn=\"{StaticResource Style.Button.Primary}\"><Setter Property=\"MinHeight\" Value=\"48\" /></Style>")]
        [InlineData("<Border Width=\"{StaticResource Icon.Width}\" />")]
        public void ANumericSize_OrANonTokenResource_IsFlagged(string xaml) =>
            Assert.Equal(new[] { "NumericSize" }, Rules(xaml));

        [Theory]
        [InlineData("<Border Width=\"{StaticResource Size.Icon.S}\" />")]
        [InlineData("<Border Width=\"{DynamicResource Size.Icon.S}\" />")]
        [InlineData("<Border Height=\"Auto\" Width=\"*\" MinWidth=\"{Binding X}\" />")]
        [InlineData("<ColumnDefinition Width=\"{StaticResource Size.Log.Col.Time}\" />")]
        [InlineData("<Ellipse StrokeThickness=\"2\" Margin=\"4\" Canvas.Left=\"7\" />")]
        public void TokensStarsAutoAndBindings_AreNotFlagged(string xaml) =>
            Assert.Empty(Rules(xaml));

        [Fact]
        public void AStaticIcon_CanBeAllowlisted_ByItsTag()
        {
            const string xaml = "<Canvas x:Name=\"BrandMark\" Width=\"64\" Height=\"64\" /><Border Width=\"10\" />";
            var allow = UiV2Allowlist.FromJson("{\"staticIcons\":[{\"file\":\"Views/Sample.xaml\",\"contains\":\"x:Name=\\\"BrandMark\\\"\",\"reason\":\"brand mark\"}]}");

            var violations = UiV2GuardScanner.Scan("Views/Sample.xaml", xaml, allow);

            Assert.Single(violations);
            Assert.Contains("Width", violations[0].Detail);
        }

        [Theory]
        [InlineData("<TextBlock FontSize=\"13\" />")]
        [InlineData("<TextBlock FontSize=\"11\" />")]
        [InlineData("<Style TargetType=\"TextBlock\" BasedOn=\"{StaticResource Style.Text.Body}\"><Setter Property=\"FontSize\" Value=\"14\" /></Style>")]
        public void AFontSizeLiteral_IsFlagged_AndOneBelowTwelveIsFlaggedAsTheFloor(string xaml) =>
            Assert.Contains("FontSizeLiteral", Rules(xaml));

        [Fact]
        public void AFontSizeBelowTwelve_IsReportedAgainstTheFloor() =>
            Assert.Contains("FontBelow12", Rules("<TextBlock FontSize=\"11\" />"));

        [Fact]
        public void AFontSizeToken_IsFine() =>
            Assert.Empty(Rules("<TextBlock FontSize=\"{StaticResource Font.Size.Caption}\" />"));

        [Theory]
        [InlineData("<TextBlock Text=\"&#9432;\" />")]        // circled i
        [InlineData("<TextBlock Text=\"&#x25BE;\" />")]       // small triangle
        [InlineData("<TextBlock Text=\"●\" />")]         // black circle
        [InlineData("<TextBlock Text=\"✓ On screen\" />")] // check mark
        [InlineData("<Button Content=\"↻ Refresh\" />")]   // refresh arrow
        [InlineData("<TextBlock Text=\"\U0001F9F9  Clear\" />")] // broom emoji
        [InlineData("<TextBlock Text=\"\U0001F4C2\" />")]       // folder emoji
        [InlineData("<TextBlock Text=\"☀\" />")]           // sun
        public void AnEmojiOrUnicodeGlyph_IsFlagged(string xaml) =>
            Assert.Equal(new[] { "GlyphIcon" }, Rules(xaml));

        [Theory]
        [InlineData("<TextBlock Text=\"DRAFT &#183; Resume\" />")] // middle dot is punctuation
        [InlineData("<TextBlock Text=\"Layout…\" />")]        // ellipsis
        [InlineData("<TextBlock Text=\"a – b\" />")]          // en dash
        [InlineData("<!-- ✓ a comment may say anything --><TextBlock Text=\"ok\" />")]
        public void PunctuationAndComments_AreNotGlyphIcons(string xaml) =>
            Assert.Empty(Rules(xaml));

        [Theory]
        [InlineData("<Image Source=\"pack://application:,,,/Assets/refresh.png\" />")]
        [InlineData("<Image Source=\"/Assets/logo.jpg\" />")]
        [InlineData("<Image Source=\"{Binding Logo}\" />")]
        [InlineData("<Border><Border.Background><ImageBrush ImageSource=\"x.png\" /></Border.Background></Border>")]
        [InlineData("<Button Tag=\"icons/play.bmp\" />")]
        public void ABitmapIcon_IsFlagged(string xaml) =>
            Assert.Equal(new[] { "BitmapIcon" }, Rules(xaml));

        [Theory]
        [InlineData("<Window Icon=\"Assets/AppIcon.ico\" />")]
        [InlineData("<Image Source=\"{Binding PreviewBitmap, Mode=OneWay}\" />")]
        [InlineData("<Image Source=\"{Binding DataContext.PreviewBitmap, RelativeSource={RelativeSource Self}}\" />")]
        public void TheAppIconAndTheLivePreviewImage_AreExempt(string xaml) =>
            Assert.Empty(Rules(xaml));

        [Theory]
        [InlineData("<ListBox ItemsSource=\"{Binding Logs}\" />")]
        [InlineData("<ListView ItemsSource=\"{Binding Rows}\" />")]
        [InlineData("<DataGrid ItemsSource=\"{Binding Rows}\" />")]
        [InlineData("<ListBox VirtualizingPanel.IsVirtualizing=\"True\" />")]
        public void AListThatIsNotVirtualized_IsFlagged(string xaml) =>
            Assert.Equal(new[] { "UnvirtualizedList" }, Rules(xaml));

        [Theory]
        [InlineData("<ListBox Style=\"{StaticResource Style.ListBox.Virtualized}\" ItemsSource=\"{Binding Logs}\" />")]
        [InlineData("<ListBox VirtualizingPanel.IsVirtualizing=\"True\" VirtualizingPanel.VirtualizationMode=\"Recycling\" VirtualizingPanel.ScrollUnit=\"Pixel\" />")]
        [InlineData("<ItemsControl ItemsSource=\"{Binding Cards}\" />")]
        public void AVirtualizedList_OrAnItemsControlGrid_IsFine(string xaml) =>
            Assert.Empty(Rules(xaml));

        [Fact]
        public void AnUnvirtualizedList_CanBeAllowlistedWithAReason()
        {
            var allow = UiV2Allowlist.FromJson("{\"unvirtualizedLists\":[{\"file\":\"Views/Sample.xaml\",\"contains\":\"x:Name=\\\"Few\\\"\",\"reason\":\"six items\"}]}");
            Assert.Empty(Rules("<ListBox x:Name=\"Few\" />", allow));
            Assert.Equal(new[] { "UnvirtualizedList" }, Rules("<ListBox x:Name=\"Many\" />", allow));
        }

        [Fact]
        public void CommentedOutMarkup_IsIgnored() =>
            Assert.Empty(Rules("<!-- <Button Width=\"48\" FontSize=\"11\"><ControlTemplate /></Button> --><Grid />"));
    }
}
