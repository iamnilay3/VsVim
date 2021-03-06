﻿using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EditorUtils;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Xunit;

namespace Vim.UnitTest
{
    public class HighlightSearchTaggerSourceTest : VimTestBase
    {
        private HighlightSearchTaggerSource _asyncTaggerSourceRaw;
        private IAsyncTaggerSource<HighlightSearchData, TextMarkerTag> _asyncTaggerSource;
        private ITextView _textView;
        private ITextBuffer _textBuffer;
        private IVimGlobalSettings _globalSettings;
        private IVimData _vimData;

        private void Create(params string[] lines)
        {
            _textView = CreateTextView(lines);
            _textBuffer = _textView.TextBuffer;
            _globalSettings = new GlobalSettings();
            _globalSettings.IgnoreCase = true;
            _globalSettings.HighlightSearch = true;
            _vimData = Vim.VimData;
            _asyncTaggerSourceRaw = new HighlightSearchTaggerSource(
                _textView,
                _globalSettings,
                _vimData,
                Vim.VimHost);
            _asyncTaggerSource = _asyncTaggerSourceRaw;
        }

        private List<ITagSpan<TextMarkerTag>> TryGetTagsPrompt(SnapshotSpan span)
        {
            IEnumerable<ITagSpan<TextMarkerTag>> tagList;
            Assert.True(_asyncTaggerSource.TryGetTagsPrompt(span, out tagList));
            return tagList.ToList();
        }

        private List<ITagSpan<TextMarkerTag>> GetTags(SnapshotSpan span)
        {
            return _asyncTaggerSource.GetTagsInBackground(
                _asyncTaggerSourceRaw.GetDataForSpan(),
                span,
                CancellationToken.None).ToList();
        }

        /// <summary>
        /// Do nothing if the search pattern is empty
        /// </summary>
        [Fact]
        public void GetTags_PatternEmpty()
        {
            Create("dog cat");
            _vimData.LastPatternData = VimUtil.CreatePatternData("");
            var ret = GetTags(_textBuffer.GetExtent());
            Assert.Equal(0, ret.Count());
        }

        /// <summary>
        /// Make sure the matches are returned
        /// </summary>
        [Fact]
        public void GetTags_WithMatch()
        {
            Create("foo is the bar");
            _vimData.LastPatternData = VimUtil.CreatePatternData("foo");
            var ret = GetTags(_textBuffer.GetExtent());
            Assert.Equal(1, ret.Count());
            Assert.Equal(new SnapshotSpan(_textBuffer.CurrentSnapshot, 0, 3), ret.Single().Span);
        }

        /// <summary>
        /// Don't return tags outside the requested span
        /// </summary>
        [Fact]
        public void GetTags_OutSideSpan()
        {
            Create("foo is the bar");
            _vimData.LastPatternData = VimUtil.CreatePatternData("foo");
            var ret = GetTags(new SnapshotSpan(_textBuffer.CurrentSnapshot, 4, 3));
            Assert.Equal(0, ret.Count());
        }

        /// <summary>
        /// It's possible for the search service to return a match of 0 length.  This is perfectly legal 
        /// and should be treated as a match of length 1.  This is how gVim does it
        ///
        /// When they are grouped thuogh return a single overarching span to avoid overloading the 
        /// editor
        /// </summary>
        [Fact]
        public void GetTags_ZeroLengthResults()
        {
            Create("cat");
            _vimData.LastPatternData = VimUtil.CreatePatternData(@"\|i\>");
            var ret = GetTags(_textBuffer.GetExtent());
            Assert.Equal(
                new [] {"cat"},
                ret.Select(x => x.Span.GetText()).ToList());
        }

        /// <summary>
        /// We can promptly say nothing when highlight is disabled
        /// </summary>
        [Fact]
        public void TryGetTagsPrompt_HighlightDisabled()
        {
            Create("dog cat");
            _vimData.LastPatternData = VimUtil.CreatePatternData("dog");
            _globalSettings.HighlightSearch = false;
            var ret = TryGetTagsPrompt(_textBuffer.GetExtent());
            Assert.Equal(0, ret.Count);
        }

        /// <summary>
        /// We can promptly say nothing when in One Time disabled
        /// </summary>
        [Fact]
        public void TryGetTagsPrompt_OneTimeDisabled()
        {
            Create("dog cat");
            _vimData.LastPatternData = VimUtil.CreatePatternData("dog");
            _asyncTaggerSourceRaw._oneTimeDisabled = true;
            var ret = TryGetTagsPrompt(_textBuffer.GetExtent());
            Assert.Equal(0, ret.Count);
        }

        /// <summary>
        /// If the ITextView is not considered visible then we shouldn't be returning any
        /// tags
        /// </summary>
        [Fact]
        public void GetTagsPrompt_NotVisible()
        {
            Create("dog cat");
            _vimData.LastPatternData = VimUtil.CreatePatternData("dog");
            _asyncTaggerSourceRaw._isVisible = false;
            var ret = TryGetTagsPrompt(_textBuffer.GetExtent());
            Assert.Equal(0, ret.Count);
        }

        /// <summary>
        /// The one time disabled event should cause a Changed event and the one time disabled
        /// flag to be set
        /// </summary>
        [Fact]
        public void Changed_OneTimeDisabled_Event()
        {
            Create("");
            Assert.False(_asyncTaggerSourceRaw._oneTimeDisabled);
            var raised = false;
            _asyncTaggerSource.Changed += delegate { raised = true; };
            _vimData.RaiseHighlightSearchOneTimeDisable();
            Assert.True(raised);
            Assert.True(_asyncTaggerSourceRaw._oneTimeDisabled);
        }

        /// <summary>
        /// The search ran should cause a Changed event if we were previously disabled
        /// </summary>
        [Fact]
        public void Changed_SearchRan_WhenDisabled()
        {
            Create("");
            var raised = false;
            _asyncTaggerSource.Changed += delegate { raised = true; };
            _vimData.RaiseHighlightSearchOneTimeDisable();
            _vimData.RaiseSearchRanEvent();
            Assert.True(raised);
        }

        /// <summary>
        /// The search ran should not cause a Changed event if we were not disabled and 
        /// the pattern didn't change from the last search.  Nothing has changed at this
        /// point
        /// </summary>
        [Fact]
        public void Changed_SearchRan_NoDifference()
        {
            Create("");
            var raised = false;
            _asyncTaggerSource.Changed += delegate { raised = true; };
            _vimData.RaiseSearchRanEvent();
            Assert.False(raised);
        }

        /// <summary>
        /// The SearchRan event should cause a Changed event if the Pattern changes
        /// </summary>
        [Fact]
        public void Changed_SearchRan_WhenPatternChanged()
        {
            Create("");
            var raised = false;
            _asyncTaggerSource.Changed += delegate { raised = true; };
            _vimData.LastPatternData = new PatternData("hello", Path.Forward);
            _vimData.RaiseSearchRanEvent();
            Assert.True(raised);
        }

        /// <summary>
        /// If the visibility of the ITextView changes it should cause a Changed event to be raised
        /// </summary>
        [Fact]
        public void Changed_IsVisibleChanged()
        {
            Create("");
            Assert.True(_asyncTaggerSourceRaw._isVisible);
            var raised = false;
            _asyncTaggerSource.Changed += delegate { raised = true; };
            VimHost.IsTextViewVisible = false;
            VimHost.RaiseIsVisibleChanged(_textView);
            Assert.False(_asyncTaggerSourceRaw._isVisible);
            Assert.True(raised);
        }

        /// <summary>
        /// The setting of the 'hlsearch' option should raise the changed event
        /// </summary>
        [Fact]
        public void HighlightSearch_RaiseChanged()
        {
            Create("");
            _globalSettings.HighlightSearch = false;
            var raised = false;
            _asyncTaggerSource.Changed += delegate { raised = true; };
            _globalSettings.HighlightSearch = true;
            Assert.True(raised);
            Assert.True(_asyncTaggerSourceRaw.IsProvidingTags);
        }

        /// <summary>
        /// The setting of the 'hlsearch' option should reset the one time disabled flag
        /// </summary>
        [Fact]
        public void HighlightSearch_ResetOneTimeDisabled()
        {
            Create("");
            _globalSettings.HighlightSearch = false;
            _asyncTaggerSourceRaw._oneTimeDisabled = true;
            _globalSettings.HighlightSearch = true;
            Assert.False(_asyncTaggerSourceRaw._oneTimeDisabled);
            Assert.True(_asyncTaggerSourceRaw.IsProvidingTags);
        }
    }
}
