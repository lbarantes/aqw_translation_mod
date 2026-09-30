using AQWMod.Localization.Sync;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class ThreeWayMergeTests
    {
        private static JObject J(string json) => JObject.Parse(json);

        private static MergeResult M(string b, string l, string r) =>
            ThreeWayMerge.Merge("f.json", J(b), J(l), J(r));

        [Fact]
        public void UserUntouched_TakesRemoteUpdate()
        {
            var m = M("{'A':'a1'}", "{'A':'a1'}", "{'A':'a2'}");
            Assert.Equal("a2", (string)m.Merged["A"]!);
            Assert.Equal(1, m.Updated);
            Assert.Empty(m.Conflicts);
        }

        [Fact]
        public void RemoteUnchanged_KeepsLocalEdit()
        {
            var m = M("{'A':'a1'}", "{'A':'mine'}", "{'A':'a1'}");
            Assert.Equal("mine", (string)m.Merged["A"]!);
            Assert.Equal(1, m.KeptLocal);
            Assert.Empty(m.Conflicts);
        }

        [Fact]
        public void BothChangedToSame_NoConflict()
        {
            var m = M("{'A':'a1'}", "{'A':'x'}", "{'A':'x'}");
            Assert.Equal("x", (string)m.Merged["A"]!);
            Assert.Empty(m.Conflicts);
        }

        [Fact]
        public void BothChangedDifferently_RemoteWins_LocalReturnedAsConflict()
        {
            var m = M("{'A':'a1'}", "{'A':'mine'}", "{'A':'theirs'}");
            Assert.Equal("theirs", (string)m.Merged["A"]!);
            var c = Assert.Single(m.Conflicts);
            Assert.Equal("A", c.Key);
            Assert.Equal("mine", (string)c.Local!);
            Assert.Equal("theirs", (string)c.Remote!);
        }

        [Fact]
        public void NewRemoteKey_IsAdded_NewLocalKey_IsKept()
        {
            var m = M("{}", "{'Mine':'m'}", "{'New':'n'}");
            Assert.Equal("n", (string)m.Merged["New"]!);
            Assert.Equal("m", (string)m.Merged["Mine"]!);
            Assert.Equal(1, m.Added);
            Assert.Equal(1, m.KeptLocal);
        }

        [Fact]
        public void RemoteDeleted_UserUntouched_IsRemoved()
        {
            var m = M("{'A':'a'}", "{'A':'a'}", "{}");
            Assert.False(m.Merged.ContainsKey("A"));
            Assert.Equal(1, m.Removed);
        }

        [Fact]
        public void RemoteDeleted_UserEdited_ConflictKeepsUserVersion()
        {
            var m = M("{'A':'a'}", "{'A':'mine'}", "{}");
            Assert.False(m.Merged.ContainsKey("A"));
            var c = Assert.Single(m.Conflicts);
            Assert.Equal("mine", (string)c.Local!);
            Assert.Null(c.Remote);
        }

        [Fact]
        public void UserDeleted_RemoteUnchanged_StaysDeleted()
        {
            var m = M("{'A':'a'}", "{}", "{'A':'a'}");
            Assert.False(m.Merged.ContainsKey("A"));
            Assert.Equal(1, m.KeptLocal);
        }

        [Fact]
        public void UserDeleted_RemoteEdited_RemoteWins_NothingToOverride()
        {
            var m = M("{'A':'a'}", "{}", "{'A':'b'}");
            Assert.Equal("b", (string)m.Merged["A"]!);
            var c = Assert.Single(m.Conflicts);
            Assert.Null(c.Local);
        }

        [Fact]
        public void NoBase_DifferingEntries_AreConflicts_IdenticalAreNot()
        {
            var m = M("{}", "{'A':'x','B':'same'}", "{'A':'y','B':'same'}");
            Assert.Equal("y", (string)m.Merged["A"]!);
            Assert.Single(m.Conflicts);
        }

        [Fact]
        public void StringAndExtendedFormOfSameText_AreEqual()
        {
            var m = M("{'A':'a1'}", "{'A':{'text':'a1','ignore':false}}", "{'A':'a2'}");
            Assert.Empty(m.Conflicts);
            Assert.Equal("a2", (string)m.Merged["A"]!);
        }

        [Fact]
        public void ContextChange_CountsAsEdit()
        {
            var m = M("{'A':'a'}", "{'A':{'text':'a','context':'skill'}}", "{'A':'a'}");
            Assert.Equal(1, m.KeptLocal);
            Assert.Equal("skill", (string)m.Merged["A"]!["context"]!);
        }

        [Fact]
        public void Order_FollowsRemoteThenLocalOnly()
        {
            var m = M("{}", "{'Z':'z','B':'b'}", "{'B':'b','A':'a'}");
            Assert.Equal(new[] { "B", "A", "Z" }, System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Select(m.Merged.Properties(), p => p.Name)));
        }
    }
}
