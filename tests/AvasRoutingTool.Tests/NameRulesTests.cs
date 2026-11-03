using System.Collections.Generic;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class NameRulesTests
    {
        private static readonly string[] NoOthers = new string[0];

        private static string? Check(string name, string current = "Current", params string[] others)
            => NameRules.Validate(name, current, others);

        [Fact]
        public void MaxLength_IsTheControlServerLimit()
        {
            Assert.Equal(19, NameRules.MaxLength);
        }

        [Fact]
        public void Empty_AsksForAName()
        {
            Assert.Equal("Enter a name", Check(""));
        }

        [Fact]
        public void WhitespaceOnly_AsksForAName()
        {
            Assert.Equal("Enter a name", Check("   "));
            Assert.Equal("Enter a name", Check("\t"));
        }

        [Fact]
        public void Null_IsHandledWithoutThrowing()
        {
            Assert.Equal("Enter a name", NameRules.Validate(null!, "Current", NoOthers));
        }

        [Fact]
        public void ThirtyThreeCharacters_AreTooLong()
        {
            var reason = Check(new string('a', 33));
            Assert.Equal("At most 19 characters (it has 33)", reason);
        }

        [Fact]
        public void TwentyCharacters_AreTooLong_AndTheReasonNamesTheLimit()
        {
            var reason = Check(new string('a', 20));
            Assert.NotNull(reason);
            Assert.Contains("19", reason);
            Assert.Equal("At most 19 characters (it has 20)", reason);
        }

        [Fact]
        public void NineteenCharacters_AreValid()
        {
            Assert.Null(Check(new string('a', 19)));
        }

        [Fact]
        public void OneCharacter_IsValid()
        {
            Assert.Null(Check("A"));
        }

        [Fact]
        public void LeadingSpace_IsRejected()
        {
            Assert.Equal("No space at the start or end", Check(" Lobby"));
        }

        [Fact]
        public void TrailingSpace_IsRejected()
        {
            Assert.Equal("No space at the start or end", Check("Lobby "));
        }

        [Fact]
        public void InternalSpace_IsValid()
        {
            Assert.Null(Check("Lobby TX 1"));
        }

        [Fact]
        public void DuplicateDifferingOnlyByCase_IsRejected()
        {
            Assert.Equal("Another device already has this name", Check("lobby tx", "Current", "LOBBY TX"));
        }

        [Fact]
        public void TrailingSpaceIsReportedBeforeADuplicate()
        {
            // "Lobby " differs from the current name "Lobby" only by a trailing space and also matches another device after trimming.
            Assert.Equal("No space at the start or end", Check("Lobby ", "Lobby", "Lobby ", "Other"));
        }

        [Fact]
        public void EqualToCurrentName_AndNotInOthers_IsValid()
        {
            Assert.Null(Check("Lobby TX", "Lobby TX", "Other", "Another"));
        }

        [Fact]
        public void EqualToCurrentName_ButAlsoInOthers_IsADuplicate()
        {
            Assert.Equal("Another device already has this name", Check("Lobby TX", "Lobby TX", "Lobby TX"));
        }

        [Fact]
        public void NonAscii_IsRejected()
        {
            const string expected = "Use plain letters, digits and symbols only";
            Assert.Equal(expected, Check("Caf\u00e9"));
            Assert.Equal(expected, Check("\u5927\u5385"));
        }

        [Fact]
        public void ControlCharacter_IsRejected()
        {
            Assert.Equal("Use plain letters, digits and symbols only", Check("A\tB"));
        }

        [Fact]
        public void Colon_IsRejected()
        {
            Assert.Equal("A colon (:) is not allowed", Check("TX:1"));
        }

        [Fact]
        public void Symbols_AreValid()
        {
            Assert.Null(Check("TX-1_a#(b)"));
        }

        [Fact]
        public void NullAndEmptyOtherNames_AreIgnored()
        {
            var others = new List<string> { null!, "", "   ", "Other" };
            Assert.Null(NameRules.Validate("Lobby", "Current", others));
            Assert.Equal("Another device already has this name", NameRules.Validate("other", "Current", others));
        }

        [Fact]
        public void EveryReason_IsOneShortLine()
        {
            var reasons = new List<string?>
            {
                Check(""), Check(" a"), Check(new string('a', 33)), Check(new string('a', 100)),
                Check("Caf\u00e9"), Check("a:b"), Check("x", "Current", "X"),
            };
            foreach (var reason in reasons)
            {
                Assert.NotNull(reason);
                Assert.DoesNotContain("\n", reason);
                Assert.DoesNotContain("\r", reason);
                Assert.True(reason!.Length <= 60, reason);
            }
        }
    }
}
