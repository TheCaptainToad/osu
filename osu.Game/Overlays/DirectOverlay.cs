﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System.Collections.Generic;
using System.Linq;
using OpenTK;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Threading;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.Direct;
using osu.Game.Overlays.SearchableList;
using OpenTK.Graphics;

namespace osu.Game.Overlays
{
    public class DirectOverlay : SearchableListOverlay<DirectTab, DirectSortCriteria, RankStatus>
    {
        private const float panel_padding = 10f;

        private APIAccess api;
        private RulesetDatabase rulesets;

        private readonly FillFlowContainer resultCountsContainer;
        private readonly OsuSpriteText resultCountsText;
        private readonly FillFlowContainer<DirectPanel> panels;

        protected override Color4 BackgroundColour => OsuColour.FromHex(@"485e74");
        protected override Color4 TrianglesColourLight => OsuColour.FromHex(@"465b71");
        protected override Color4 TrianglesColourDark => OsuColour.FromHex(@"3f5265");

        protected override SearchableListHeader<DirectTab> CreateHeader() => new Header();
        protected override SearchableListFilterControl<DirectSortCriteria, RankStatus> CreateFilterControl() => new FilterControl();

        private IEnumerable<BeatmapSetInfo> beatmapSets;
        public IEnumerable<BeatmapSetInfo> BeatmapSets
        {
            get { return beatmapSets; }
            set
            {
                if (beatmapSets?.Equals(value) ?? false) return;
                beatmapSets = value;

                if (BeatmapSets == null)
                {
                    foreach (var p in panels.Children)
                    {
                        p.FadeOut(200);
                        p.Expire();
                    }

                    return;
                }

                recreatePanels(Filter.DisplayStyleControl.DisplayStyle.Value);
            }
        }

        private ResultCounts resultAmounts;
        public ResultCounts ResultAmounts
        {
            get { return resultAmounts; }
            set
            {
                if (value == ResultAmounts) return;
                resultAmounts = value;

                updateResultCounts();
            }
        }

        public DirectOverlay()
        {
            RelativeSizeAxes = Axes.Both;

            // osu!direct colours are not part of the standard palette

            FirstWaveColour = OsuColour.FromHex(@"19b0e2");
            SecondWaveColour = OsuColour.FromHex(@"2280a2");
            ThirdWaveColour = OsuColour.FromHex(@"005774");
            FourthWaveColour = OsuColour.FromHex(@"003a4e");

            ScrollFlow.Children = new Drawable[]
            {
                resultCountsContainer = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Margin = new MarginPadding { Top = 5 },
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "Found ",
                            TextSize = 15,
                        },
                        resultCountsText = new OsuSpriteText
                        {
                            TextSize = 15,
                            Font = @"Exo2.0-Bold",
                        },
                    }
                },
                panels = new FillFlowContainer<DirectPanel>
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Spacing = new Vector2(panel_padding),
                    Margin = new MarginPadding { Top = 10 },
                },
            };

            Filter.Search.Current.ValueChanged += text => { if (text != string.Empty) Header.Tabs.Current.Value = DirectTab.Search; };
            ((FilterControl)Filter).Ruleset.ValueChanged += ruleset => Scheduler.AddOnce(updateSearch);
            Filter.DisplayStyleControl.DisplayStyle.ValueChanged += recreatePanels;
            Filter.DisplayStyleControl.Dropdown.Current.ValueChanged += rankStatus => Scheduler.AddOnce(updateSearch);

            Header.Tabs.Current.ValueChanged += tab =>
            {
                if (tab != DirectTab.Search)
                {
                    Filter.Search.Text = lastQuery = string.Empty;
                    Filter.Tabs.Current.Value = (DirectSortCriteria)Header.Tabs.Current.Value;
                    Scheduler.AddOnce(updateSearch);
                }
            };

            Filter.Search.OnCommit = (sender, text) =>
            {
                lastQuery = Filter.Search.Text;
                updateSets();
            };

            Filter.Tabs.Current.ValueChanged += sortCriteria =>
            {
                if (Header.Tabs.Current.Value != DirectTab.Search && sortCriteria != (DirectSortCriteria)Header.Tabs.Current.Value)
                    Header.Tabs.Current.Value = DirectTab.Search;

                Scheduler.AddOnce(updateSearch);
            };

            updateResultCounts();
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, APIAccess api, RulesetDatabase rulesets)
        {
            this.api = api;
            this.rulesets = rulesets;
            resultCountsContainer.Colour = colours.Yellow;
        }

        private void updateResultCounts()
        {
            resultCountsContainer.FadeTo(ResultAmounts == null ? 0f : 1f, 200, EasingTypes.Out);
            if (ResultAmounts == null) return;

            resultCountsText.Text = pluralize("Artist", ResultAmounts.Artists) + ", " +
                                    pluralize("Song", ResultAmounts.Songs) + ", " +
                                    pluralize("Tag", ResultAmounts.Tags);
        }

        private string pluralize(string prefix, int value)
        {
            return $@"{value} {prefix}" + (value == 1 ? string.Empty : @"s");
        }

        private void recreatePanels(PanelDisplayStyle displayStyle)
        {
            if (BeatmapSets == null) return;
            panels.ChildrenEnumerable = BeatmapSets.Select(b => displayStyle == PanelDisplayStyle.Grid ? (DirectPanel)new DirectGridPanel(b) { Width = 400 } : new DirectListPanel(b));
        }

        private GetBeatmapSetsRequest getSetsRequest;
        private string lastQuery = string.Empty;
        private void updateSearch()
        {
            if (!IsLoaded || Header.Tabs.Current.Value == DirectTab.Search && (Filter.Search.Text == string.Empty || lastQuery == string.Empty)) return;

            BeatmapSets = null;
            ResultAmounts = null;
            getSetsRequest?.Cancel();

            if (api == null) return;

            getSetsRequest = new GetBeatmapSetsRequest(lastQuery,
                                                       ((FilterControl)Filter).Ruleset.Value,
                                                       Filter.DisplayStyleControl.Dropdown.Current.Value,
                                                       Filter.Tabs.Current.Value); //todo: sort direction (?)

            getSetsRequest.Success += r =>
            {
                BeatmapSets = r?.Select(response => response.ToBeatmapSet(rulesets));
                if (BeatmapSets == null) return;

                var artists = new List<string>();
                var songs = new List<string>();
                var tags = new List<string>();
                foreach (var s in BeatmapSets)
                {
                    artists.Add(s.Metadata.Artist);
                    songs.Add(s.Metadata.Title);
                    tags.AddRange(s.Metadata.Tags.Split(' '));
                }

                ResultAmounts = new ResultCounts(distinctCount(artists),
                                                 distinctCount(songs),
                                                 distinctCount(tags));
            };
            api.Queue(getSetsRequest);
        }

        private int distinctCount(List<string> list) => list.Distinct().ToArray().Length;

        public class ResultCounts
        {
            public readonly int Artists;
            public readonly int Songs;
            public readonly int Tags;

            public ResultCounts(int artists, int songs, int tags)
            {
                Artists = artists;
                Songs = songs;
                Tags = tags;
            }
        }
    }
}
