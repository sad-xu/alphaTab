﻿/*
 * This file is part of alphaTab.
 * Copyright (c) 2014, Daniel Kuschny and Contributors, All rights reserved.
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3.0 of the License, or at your option any later version.
 * 
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library.
 */
using System;
using AlphaTab.Collections;
using AlphaTab.Model;
using AlphaTab.Platform;
using AlphaTab.Rendering.Layout;
using AlphaTab.Rendering.Utils;

namespace AlphaTab.Rendering.Staves
{
    public class StaveTrackGroup
    {
        public Track Track { get; set; }
        public StaveGroup StaveGroup { get; set; }
        public FastList<Staff> Staves { get; set; }

        public Staff FirstStaffInAccolade { get; set; }
        public Staff LastStaffInAccolade { get; set; }

        public StaveTrackGroup(StaveGroup staveGroup, Track track)
        {
            StaveGroup = staveGroup;
            Track = track;
            Staves = new FastList<Staff>();
        }
    }

    public class AddBarsToStaveGroupResult
    {
        public float Width { get; set; }
        public bool IsLinkedToPrevious { get; set; }
    }

    /// <summary>
    /// A Staff consists of a list of different staves and groups
    /// them using an accolade. 
    /// </summary>
    public class StaveGroup
    {
        private const float AccoladeLabelSpacing = 10;

        private Staff _firstStaffInAccolade;
        private Staff _lastStaffInAccolade;

        public float X { get; set; }
        public float Y { get; set; }
        public int Index { get; set; }

        private bool _accoladeSpacingCalculated;
        public float AccoladeSpacing { get; set; }

        /// <summary>
        /// Indicates whether this line is full or not. If the line is full the
        /// bars can be aligned to the maximum width. If the line is not full 
        /// the bars will not get stretched.
        /// </summary>
        public bool IsFull { get; set; }

        /// <summary>
        /// The width that the content bars actually need
        /// </summary>
        public float Width { get; set; }

        public FastList<MasterBar> MasterBars { get; set; }

        public FastList<StaveTrackGroup> Staves { get; set; }
        private FastList<Staff> _allStaves;

        public ScoreLayout Layout { get; set; }
        public BarHelpersGroup Helpers { get; set; }

        public StaveGroup()
        {
            MasterBars = new FastList<MasterBar>();
            Staves = new FastList<StaveTrackGroup>();
            _allStaves = new FastList<Staff>();
            Width = 0;
            Index = 0;
            _accoladeSpacingCalculated = false;
            AccoladeSpacing = 0;

            Helpers = new BarHelpersGroup();
        }

        public int LastBarIndex
        {
            get
            {
                return MasterBars[MasterBars.Count - 1].Index;
            }
        }

        public AddBarsToStaveGroupResult AddBars(Track[] tracks, int barIndex)
        {
            if (tracks.Length == 0) return null;

            var result = new AddBarsToStaveGroupResult();
            var score = tracks[0].Score;
            var masterBar = score.MasterBars[barIndex];
            MasterBars.Add(masterBar);

            Helpers.BuildHelpers(tracks, barIndex);

            if (!_accoladeSpacingCalculated && Index == 0)
            {
                _accoladeSpacingCalculated = true;
                var canvas = Layout.Renderer.Canvas;
                var res = Layout.Renderer.RenderingResources.EffectFont;
                canvas.Font = res;
                for (var i = 0; i < tracks.Length; i++)
                {
                    AccoladeSpacing = Math.Max(AccoladeSpacing, canvas.MeasureText(tracks[i].ShortName));
                }
                AccoladeSpacing += (2 * AccoladeLabelSpacing);
                Width += AccoladeSpacing;
            }

            // add renderers
            var maxSizes = new BarSizeInfo();
            for (int i = 0, j = Staves.Count; i < j; i++)
            {
                var g = Staves[i];
                for (int k = 0, l = g.Staves.Count; k < l; k++)
                {
                    var s = g.Staves[k];
                    s.AddBar(g.Track.Staves[s.ModelStaff.Index].Bars[barIndex]);
                    s.BarRenderers[s.BarRenderers.Count - 1].RegisterMaxSizes(maxSizes);
                    if (s.BarRenderers[s.BarRenderers.Count - 1].IsLinkedToPrevious)
                    {
                        result.IsLinkedToPrevious = true;
                    }
                }
            }

            // ensure same widths of new renderer
            var realWidth = 0f;
            for (int i = 0, j = _allStaves.Count; i < j; i++)
            {
                var s = _allStaves[i];
                s.BarRenderers[s.BarRenderers.Count - 1].ApplySizes(maxSizes);
                if (s.BarRenderers[s.BarRenderers.Count - 1].Width > realWidth)
                {
                    realWidth = s.BarRenderers[s.BarRenderers.Count - 1].Width;
                }
            }

            Width += realWidth;
            result.Width = realWidth;

            return result;
        }

        public BarRendererBase GetBarRenderer(int barIndex)
        {
            var stave = _firstStaffInAccolade;
            if (barIndex >= stave.BarRenderers.Count)
            {
                return null;
            }
            return stave.BarRenderers[barIndex];
        }

        private StaveTrackGroup GetStaveTrackGroup(Track track)
        {
            for (int i = 0, j = Staves.Count; i < j; i++)
            {
                var g = Staves[i];
                if (g.Track == track)
                {
                    return g;
                }
            }
            return null;
        }

        public void AddStave(Track track, Staff staff)
        {
            var group = GetStaveTrackGroup(track);
            if (group == null)
            {
                group = new StaveTrackGroup(this, track);
                Staves.Add(group);
            }

            staff.StaveTrackGroup = group;
            staff.StaveGroup = this;
            staff.Index = _allStaves.Count;
            _allStaves.Add(staff);
            group.Staves.Add(staff);

            if (staff.IsInAccolade)
            {
                if (_firstStaffInAccolade == null)
                {
                    _firstStaffInAccolade = staff;
                    staff.IsFirstInAccolade = true;
                }
                if (group.FirstStaffInAccolade == null)
                {
                    group.FirstStaffInAccolade = staff;
                }
                if (_lastStaffInAccolade == null)
                {
                    _lastStaffInAccolade = staff;
                    staff.IsLastInAccolade = true;
                }

                if (_lastStaffInAccolade != null) { _lastStaffInAccolade.IsLastInAccolade = false; }
                _lastStaffInAccolade = staff;
                _lastStaffInAccolade.IsLastInAccolade = true;
                group.LastStaffInAccolade = staff;
            }
        }

        public float Height
        {
            get
            {
                return _allStaves[_allStaves.Count - 1].Y + _allStaves[_allStaves.Count - 1].Height;
            }
        }

        public void RevertLastBar()
        {
            if (MasterBars.Count > 1)
            {
                MasterBars.RemoveAt(MasterBars.Count - 1);
                var w = 0f;
                for (int i = 0, j = _allStaves.Count; i < j; i++)
                {
                    var s = _allStaves[i];
                    w = Math.Max(w, s.BarRenderers[s.BarRenderers.Count - 1].Width);
                    s.RevertLastBar();
                }
                Width -= w;
            }
        }

        public void ScaleToWidth(float width)
        {
            for (int i = 0, j = _allStaves.Count; i < j; i++)
            {
                _allStaves[i].ScaleToWidth(width);
            }
            Width = width;
        }

        public void Paint(float cx, float cy, ICanvas canvas)
        {
            PaintPartial(cx + X, cy + Y, canvas, 0, MasterBars.Count);
        }

        public void PaintPartial(float cx, float cy, ICanvas canvas, int startIndex, int count)
        {
            for (int i = 0, j = _allStaves.Count; i < j; i++)
            {
                _allStaves[i].Paint(cx, cy, canvas, startIndex, count);
            }

            var res = Layout.Renderer.RenderingResources;

            if (Staves.Count > 0 && startIndex == 0)
            {
                //
                // Draw start grouping
                // 

                if (_firstStaffInAccolade != null && _lastStaffInAccolade != null)
                {
                    //
                    // draw grouping line for all staves
                    //

                    var firstStart = cy + _firstStaffInAccolade.Y + _firstStaffInAccolade.StaveTop + _firstStaffInAccolade.TopSpacing + _firstStaffInAccolade.TopOverflow;
                    var lastEnd = cy + _lastStaffInAccolade.Y + _lastStaffInAccolade.TopSpacing + _lastStaffInAccolade.TopOverflow
                                         + _lastStaffInAccolade.StaveBottom;

                    var acooladeX = cx + _firstStaffInAccolade.X;

                    canvas.Color = res.BarSeperatorColor;

                    canvas.BeginPath();
                    canvas.MoveTo(acooladeX, firstStart);
                    canvas.LineTo(acooladeX, lastEnd);
                    canvas.Stroke();
                }

                //
                // Draw accolade for each track group
                // 
                canvas.Font = res.EffectFont;
                for (int i = 0, j = Staves.Count; i < j; i++)
                {
                    var g = Staves[i];
                    if(g.FirstStaffInAccolade != null && g.LastStaffInAccolade != null)
                    {
                        var firstStart = cy + g.FirstStaffInAccolade.Y + g.FirstStaffInAccolade.StaveTop + g.FirstStaffInAccolade.TopSpacing + g.FirstStaffInAccolade.TopOverflow;
                        var lastEnd = cy + g.LastStaffInAccolade.Y + g.LastStaffInAccolade.TopSpacing + g.LastStaffInAccolade.TopOverflow
                                             + g.LastStaffInAccolade.StaveBottom;

                        var acooladeX = cx + g.FirstStaffInAccolade.X;

                        var barSize = (3 * Layout.Renderer.Settings.Scale);
                        var barOffset = barSize;

                        var accoladeStart = firstStart - (barSize * 4);
                        var accoladeEnd = lastEnd + (barSize * 4);

                        // text
                        if (Index == 0)
                        {
                            canvas.FillText(g.Track.ShortName, cx + (AccoladeLabelSpacing * Layout.Scale), firstStart);
                        }

                        // rect
                        canvas.FillRect(acooladeX - barOffset - barSize, accoladeStart, barSize, accoladeEnd - accoladeStart);

                        var spikeStartX = acooladeX - barOffset - barSize;
                        var spikeEndX = acooladeX + barSize * 2;

                        // top spike
                        canvas.BeginPath();
                        canvas.MoveTo(spikeStartX, accoladeStart);
                        canvas.BezierCurveTo(spikeStartX, accoladeStart, spikeStartX, accoladeStart, spikeEndX, accoladeStart - barSize);
                        canvas.BezierCurveTo(acooladeX, accoladeStart + barSize, spikeStartX, accoladeStart + barSize, spikeStartX, accoladeStart + barSize);
                        canvas.ClosePath();
                        canvas.Fill();

                        // bottom spike 
                        canvas.BeginPath();
                        canvas.MoveTo(spikeStartX, accoladeEnd);
                        canvas.BezierCurveTo(spikeStartX, accoladeEnd, acooladeX, accoladeEnd, spikeEndX, accoladeEnd + barSize);
                        canvas.BezierCurveTo(acooladeX, accoladeEnd - barSize, spikeStartX, accoladeEnd - barSize, spikeStartX, accoladeEnd - barSize);
                        canvas.ClosePath();

                        canvas.Fill();
                    }
                }
            }
        }

        public void FinalizeGroup(ScoreLayout scoreLayout)
        {
            float currentY = 0;
            for (int i = 0, j = _allStaves.Count; i < j; i++)
            {
                _allStaves[i].X = AccoladeSpacing;
                _allStaves[i].Y = (currentY);
                _allStaves[i].FinalizeStave(scoreLayout);
                currentY += _allStaves[i].Height;
            }
        }

        public void BuildBoundingsLookup(BoundingsLookup lookup)
        {
            var visualTop = Y + _firstStaffInAccolade.Y;
            var visualBottom = Y + _lastStaffInAccolade.Y + _lastStaffInAccolade.Height;
            var realTop = Y + _allStaves[0].Y;
            var realBottom = Y + _allStaves[_allStaves.Count - 1].Y + _allStaves[_allStaves.Count - 1].Height;

            var visualHeight = visualBottom - visualTop;
            var realHeight = realBottom - realTop;
            for (int i = 0, j = _firstStaffInAccolade.BarRenderers.Count; i < j; i++)
            {
                _firstStaffInAccolade.BarRenderers[i].BuildBoundingsLookup(lookup, visualTop, visualHeight, realTop, realHeight, X);
            }
        }
    }
}
