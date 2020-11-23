﻿using ARKBreedingStats.species;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ARKBreedingStats
{
    internal class RadarChart : PictureBox
    {
        private int _maxLevel; // outer border of graph
        private List<Point> _maxPs, _ps; // coords of outer points
        private int _maxR, _xm, _ym; // max-radius, centerX, centerY
        private int[] _oldLevels;
        private PathGradientBrush _grBrushBg, _grBrushFg;
        private int _step;
        private const int DisplayedStats = 7;
        private const double AnglePerStat = Math.PI * 2 / DisplayedStats;
        private const double AngleOffset = Math.PI / 2;

        public RadarChart()
        {
            _oldLevels = new int[DisplayedStats];
            InitializeVariables(50);
            SizeMode = PictureBoxSizeMode.Zoom;
        }

        /// <summary>
        /// Initialize with a new max chart level.
        /// </summary>
        /// <param name="maxLevel"></param>
        public void InitializeVariables(int maxLevel)
        {
            this._maxLevel = maxLevel;
            _maxR = Math.Min(Width, Height) / 2;
            _xm = _maxR + 1;
            _ym = _maxR + 1;
            _maxR -= 15;

            _step = (int)Math.Round(maxLevel / 25d);
            if (_step < 1) _step = 1;
            _step *= 5;

            _maxPs = new List<Point>();
            _ps = new List<Point>();
            int r = 0;
            for (int s = 0; s < DisplayedStats; s++)
            {
                double angle = AnglePerStat * s - AngleOffset;
                _ps.Add(new Point(_xm + (int)(r * Math.Cos(angle)), _ym + (int)(r * Math.Sin(angle))));

                _maxPs.Add(new Point(_xm + (int)(_maxR * Math.Cos(angle)), _ym + (int)(_maxR * Math.Sin(angle))));
            }

            // color gradient
            GraphicsPath path = new GraphicsPath();
            path.AddEllipse(_xm - _maxR, _ym - _maxR, 2 * _maxR + 1, 2 * _maxR + 1);
            _grBrushBg = new PathGradientBrush(path);
            _grBrushFg = new PathGradientBrush(path);

            Color[] colorsBg =
            {
                    Color.FromArgb(0, 90, 0),
                    Color.FromArgb(90, 90, 0),
                    Color.FromArgb(90, 0, 0)
            };

            Color[] colorsFg =
            {
                    Color.FromArgb(0, 180, 0),
                    Color.FromArgb(180, 180, 0),
                    Color.FromArgb(180, 0, 0)
            };

            float[] relativePositions = { 0, 0.45f, 1 };

            ColorBlend colorBlendBg = new ColorBlend
            {
                Colors = colorsBg,
                Positions = relativePositions
            };
            _grBrushBg.InterpolationColors = colorBlendBg;

            ColorBlend colorBlendFg = new ColorBlend
            {
                Colors = colorsFg,
                Positions = relativePositions
            };
            _grBrushFg.InterpolationColors = colorBlendFg;

            SetLevels();
        }

        /// <summary>
        /// Draws a chart with the given levels.
        /// </summary>
        /// <param name="levels">If null, the previous values are redrawn.</param>
        public void SetLevels(int[] levels = null)
        {
            if ((levels != null && levels.Length <= 6) || _maxR <= 5) return;

            Bitmap bmp = new Bitmap(Width, Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // the indices of the displayed stats
                var levelIndices = new[]
                {
                    (int) StatNames.Health, (int) StatNames.Stamina, (int) StatNames.Oxygen, (int) StatNames.Food,
                    (int) StatNames.Weight, (int) StatNames.MeleeDamageMultiplier, (int) StatNames.SpeedMultiplier
                };

                for (int s = 0; s < levelIndices.Length; s++)
                {
                    if (levels == null || levels[levelIndices[s]] != _oldLevels[s])
                    {
                        if (levels != null)
                            _oldLevels[s] = levels[levelIndices[s]];
                        int r = _oldLevels[s] * _maxR / _maxLevel;
                        if (r < 0) r = 0;
                        if (r > _maxR) r = _maxR;
                        double angle = AnglePerStat * s - AngleOffset;
                        _ps[s] = new Point(_xm + (int)(r * Math.Cos(angle)), _ym + (int)(r * Math.Sin(angle)));
                    }
                }

                g.FillEllipse(_grBrushBg, _xm - _maxR, _ym - _maxR, 2 * _maxR + 1, 2 * _maxR + 1);

                Pen penL = new Pen(Color.FromArgb(128, 255, 255, 255));
                g.FillPolygon(_grBrushFg, _ps.ToArray());
                g.DrawPolygon(penL, _ps.ToArray());

                double stepFactor = (double)_step / _maxLevel;
                for (int r = 0; r < 5; r++)
                    g.DrawEllipse(new Pen(Utils.GetColorFromPercent((int)(100 * r * stepFactor), -0.4)),
                        (int)(_xm - _maxR * r * stepFactor), (int)(_ym - _maxR * r * stepFactor),
                        (int)(2 * _maxR * r * stepFactor + 1), (int)(2 * _maxR * r * stepFactor + 1));
                g.DrawEllipse(new Pen(Utils.GetColorFromPercent(100, -0.4)), _xm - _maxR, _ym - _maxR,
                    2 * _maxR + 1, 2 * _maxR + 1);

                Pen pen = new Pen(Color.Black);
                for (int s = 0; s < levelIndices.Length; s++)
                {
                    pen.Width = 1;
                    pen.Color = Color.Gray;
                    g.DrawLine(pen, _xm, _ym, _maxPs[s].X, _maxPs[s].Y);
                    Color cl = Utils.GetColorFromPercent(100 * _oldLevels[s] / _maxLevel);
                    pen.Color = cl;
                    pen.Width = 3;
                    g.DrawLine(pen, _xm, _ym, _ps[s].X, _ps[s].Y);
                    Brush b = new SolidBrush(cl);
                    g.FillEllipse(b, _ps[s].X - 4, _ps[s].Y - 4, 8, 8);
                    g.DrawEllipse(penL, _ps[s].X - 4, _ps[s].Y - 4, 8, 8);
                    b.Dispose();
                }

                for (int r = 1; r < 5; r++)
                    g.DrawString((_step * r).ToString("N0"), new Font("Microsoft Sans Serif", 8f),
                        new SolidBrush(Color.FromArgb(190, 255, 255, 255)), _xm - 8, _ym - 6 + r * _maxR / 5);
                g.DrawString((_maxLevel).ToString("N0"), new Font("Microsoft Sans Serif", 8f),
                    new SolidBrush(Color.FromArgb(190, 255, 255, 255)), _xm - 8, _ym - 11 + _maxR);

                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                for (int s = 0; s < levelIndices.Length; s++)
                {
                    double angle = AnglePerStat * s - AngleOffset;
                    g.DrawString(Utils.StatName(levelIndices[s], true), new Font("Microsoft Sans Serif", 8f),
                        new SolidBrush(Color.Black), _xm - 9 + (int)((_maxR + 10) * Math.Cos(angle)),
                        _ym - 5 + (int)((_maxR + 10) * Math.Sin(angle)));
                }
            }

            Image?.Dispose();
            Image = bmp;
        }
    }
}
