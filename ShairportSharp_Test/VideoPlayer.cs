﻿using AirPlayer.Common.DirectShow;
using DirectShow;
using DirectShow.Helper;
using ShairportSharp.Airplay;
using ShairportSharp.Mirroring;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ShairportSharp_Test
{
    public class rotPlayer : DSFilePlayback
    {
        DirectShow.DsROTEntry _rot = null;
        protected override HRESULT OnInitInterfaces()
        {
            _rot = new DirectShow.DsROTEntry(m_GraphBuilder);
            return base.OnInitInterfaces();
        }

        public override void Dispose()
        {
            if (_rot != null)
            {
                try
                {
                    _rot.Dispose();
                }
                catch { }
                _rot = null;
            }
            base.Dispose();
        }
    }

    public class MirrorPlayer : DSFilePlayback
    {
        MirroringStream stream;
        DsROTEntry _rot = null;

        public MirrorPlayer(MirroringStream stream)
        {
            this.stream = stream;
        }

        protected override HRESULT OnInitInterfaces()
        {
            _rot = new DsROTEntry(m_GraphBuilder);

            var sourceFilter = new MirroringSourceFilter(stream);
            int hr = m_GraphBuilder.AddFilter(sourceFilter, sourceFilter.Name);
            new HRESULT(hr).Throw();

            var lavVideo = new DSFilter(new Guid("{EE30215D-164F-4A92-A4EB-9D4C13390F9F}"));
            hr = m_GraphBuilder.AddFilter(lavVideo.Value, "LAV Video Decoder");
            //var lavVideo = new DSFilter(new Guid("{212690FB-83E5-4526-8FD7-74478B7939CD}"));
            //hr = m_GraphBuilder.AddFilter(lavVideo.Value, "Microsoft DTV-DVD Video Decoder");
            new HRESULT(hr).Throw();

            DSFilter source2 = new DSFilter(sourceFilter);
            hr = m_GraphBuilder.Connect(source2.OutputPin.Value, lavVideo.InputPin.Value);
            hr = m_GraphBuilder.Render(lavVideo.OutputPin.Value);
            return new HRESULT(hr);
        }

        public override void Dispose()
        {
            if (_rot != null)
            {
                try
                {
                    _rot.Dispose();
                }
                catch { }
                _rot = null;
            }
            base.Dispose();
        }
    }
}