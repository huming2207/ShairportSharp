﻿using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.Profile;
using ShairportSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using DirectShow;
using DShowNET.Helper;
using DirectShow.Helper;
using ShairportSharp.Remote;

namespace AirPlayer
{
    class Player : IPlayer
    {
        public enum PlayState
        {
            Init,
            Playing,
            Paused,
            Ended
        }

        private string m_strCurrentFile = "";
        private PlayState m_state = PlayState.Init;
        private int m_iVolume = 100;
        private bool m_bNotifyPlaying = true;

        object positionLock = new object();
        uint startStamp;
        uint stopStamp;
        uint nextStartStamp;
        uint nextStopStamp;
        double duration;

        private IGraphBuilder graphBuilder;

        private DsROTEntry _rotEntry = null;

        /// <summary> control interface. </summary>
        private IMediaControl mediaCtrl;

        /// <summary> graph event interface. </summary>
        private IMediaEventEx mediaEvt;

        /// <summary> seek interface for positioning in stream. </summary>
        private IMediaSeeking mediaSeek;

        /// <summary> seek interface to set position in stream. </summary>
        private IMediaPosition mediaPos;

        /// <summary> video preview window interface. </summary>
        /// <summary> audio interface used to control volume. </summary>
        private IBasicAudio basicAudio;

        PlayerSettings settings;

        private const int WM_GRAPHNOTIFY = 0x00008001; // message from graph

        private const int WS_CHILD = 0x40000000; // attributes for video window
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

        public Player(PlayerSettings settings) 
        {
            this.settings = settings;
        }

        public override bool Play(string strFile)
        {
            m_iVolume = 100;
            m_bNotifyPlaying = true;
            m_state = PlayState.Init;
            m_strCurrentFile = strFile;

            VideoRendererStatistics.VideoState = VideoRendererStatistics.State.VideoPresent;
            Logger.Instance.Info("ShairportPlayer.play {0}", strFile);

            lock (typeof(Player))
            {
                CloseInterfaces();
                if (!GetInterfaces())
                {
                    m_strCurrentFile = "";
                    return false;
                }
                int hr = mediaEvt.SetNotifyWindow(GUIGraphicsContext.ActiveForm, WM_GRAPHNOTIFY, IntPtr.Zero);
                if (hr < 0)
                {
                    m_strCurrentFile = "";
                    CloseInterfaces();
                    return false;
                }
                
                _rotEntry = new DsROTEntry((IFilterGraph)graphBuilder);

                hr = mediaCtrl.Run();
                if (hr < 0)
                {
                    m_strCurrentFile = "";
                    CloseInterfaces();
                    return false;
                }
                //        mediaPos.put_CurrentPosition(4*60);
                GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_PLAYBACK_STARTED, 0, 0, 0, 0, 0, null);
                msg.Label = strFile;
                GUIWindowManager.SendThreadMessage(msg);
            }
            m_state = PlayState.Playing;
            return true;
        }


        private void MovieEnded(bool bManualStop)
        {
            // this is triggered only if movie has ended
            // ifso, stop the movie which will trigger MovieStopped
            if (null != mediaCtrl)
            {
                Logger.Instance.Info("ShairportPlayer.ended {0}", m_strCurrentFile);
                m_strCurrentFile = "";
                if (!bManualStop)
                {
                    m_state = PlayState.Ended;
                }
                else
                {
                    m_state = PlayState.Init;
                }
            }
        }

        internal void UpdateDurationInfo(uint startStamp, uint stopStamp)
        {
            lock (positionLock)
            {
                this.startStamp = startStamp;
                this.stopStamp = stopStamp;
                duration = (stopStamp - startStamp) / 44100.0;
            }
        }

        public override double Duration
        {
            get
            {
                lock (positionLock)
                    return duration;
            }
        }

        public override double CurrentPosition
        {
            get
            {
                uint currentTimeStamp = settings.GetLastTimeStamp();
                double position;
                lock (positionLock)
                    position = currentTimeStamp < startStamp ? 0 : (currentTimeStamp - startStamp) / 44100.0;

                return position;
            }
        }

        public override void Pause()
        {
            if (m_state == PlayState.Paused)
            {
                //settings.SendCommand(RemoteCommand.Play);
                mediaCtrl.Run();
                m_state = PlayState.Playing;
            }
            else if (m_state == PlayState.Playing)
            {
                //settings.SendCommand(RemoteCommand.Pause);
                m_state = PlayState.Paused;
                mediaCtrl.Pause();
            }
        }

        public override bool Paused
        {
            get { return (m_state == PlayState.Paused); }
        }

        public override bool Playing
        {
            get { return (m_state == PlayState.Playing || m_state == PlayState.Paused); }
        }

        public override bool Stopped
        {
            get { return (m_state == PlayState.Init); }
        }

        public override string CurrentFile
        {
            get { return m_strCurrentFile; }
        }

        public override void Stop()
        {
            if (m_state != PlayState.Init)
            {
                //settings.SendCommand(RemoteCommand.Stop);
                mediaCtrl.StopWhenReady();
                MovieEnded(true);
            }
        }

        public override int Volume
        {
            get { return m_iVolume; }
            set
            {
                if (m_iVolume != value)
                {
                    m_iVolume = value;
                    if (m_state != PlayState.Init)
                    {
                        if (basicAudio != null)
                        {
                            // Divide by 100 to get equivalent decibel value. For example, –10,000 is –100 dB. 
                            float fPercent = (float)m_iVolume / 100.0f;
                            int iVolume = (int)((DirectShowVolume.VOLUME_MAX - DirectShowVolume.VOLUME_MIN) * fPercent);
                            basicAudio.put_Volume((iVolume - DirectShowVolume.VOLUME_MIN));
                        }
                    }
                }
            }
        }

        public override bool HasVideo
        {
            get { return false; }
        }

        /// <summary> create the used COM components and get the interfaces. </summary>
        private bool GetInterfaces()
        {
            int iStage = 1;
            string audioDevice;
            using (Settings xmlreader = new MPSettings())
            {
                audioDevice = xmlreader.GetValueAsString("audioplayer", "sounddevice", "Default DirectSound Device");
            }
            if (audioDevice == "Default Sound Device")
                audioDevice = "Default DirectSound Device";

            int hr;
            try
            {
                graphBuilder = (IGraphBuilder)new FilterGraph();
                iStage = 5;
                DirectShow.Helper.Utils.AddFilterByName(graphBuilder, DirectShow.FilterCategory.AudioRendererCategory, audioDevice);

                var sourceFilter = new GenericPushSourceFilter(settings.Source, settings.GetMediaType());
                hr = graphBuilder.AddFilter(sourceFilter, sourceFilter.Name);
                new HRESULT(hr).Throw();
                DSFilter source2 = new DSFilter(sourceFilter);
                hr = source2.OutputPin.Render();
                new HRESULT(hr).Throw();
                
                if (hr != 0)
                {
                    Error.SetError("Unable to play file", "Missing codecs to play this file");
                    return false;
                }
                iStage = 6;
                mediaCtrl = (IMediaControl)graphBuilder;

                iStage = 7;
                mediaEvt = (IMediaEventEx)graphBuilder;
                iStage = 8;
                mediaSeek = (IMediaSeeking)graphBuilder;
                iStage = 9;
                mediaPos = (IMediaPosition)graphBuilder;
                iStage = 10;
                basicAudio = graphBuilder as IBasicAudio;
                iStage = 11;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Info("Can not start {0} stage:{1} err:{2} stack:{3}",
                         m_strCurrentFile, iStage,
                         ex.Message,
                         ex.StackTrace);
                return false;
            }
        }
        
        /// <summary> do cleanup and release DirectShow. </summary>
        private void CloseInterfaces()
        {
            int hr;
            try
            {
                if (mediaCtrl != null)
                {
                    int counter = 0;
                    FilterState state;
                    hr = mediaCtrl.Stop();
                    hr = mediaCtrl.GetState(10, out state);
                    while (state != FilterState.Stopped || GUIGraphicsContext.InVmr9Render)
                    {
                        System.Threading.Thread.Sleep(100);
                        hr = mediaCtrl.GetState(10, out state);
                        counter++;
                        if (counter >= 30)
                        {
                            if (state != FilterState.Stopped)
                                Logger.Instance.Debug("ShairportPlayer: graph still running");
                            if (GUIGraphicsContext.InVmr9Render)
                                Logger.Instance.Debug("ShairportPlayer: in renderer");
                            break;
                        }
                    }
                    mediaCtrl = null;
                }

                m_state = PlayState.Init;

                if (mediaEvt != null)
                {
                    hr = mediaEvt.SetNotifyWindow(IntPtr.Zero, WM_GRAPHNOTIFY, IntPtr.Zero);
                    mediaEvt = null;
                }

                mediaSeek = null;
                mediaPos = null;
                basicAudio = null;
                
                if (graphBuilder != null)
                {
                    if (_rotEntry != null)
                    {
                        _rotEntry.Dispose();
                        _rotEntry = null;
                    }
                    Marshal.ReleaseComObject(graphBuilder);
                    graphBuilder = null;
                }

                m_state = PlayState.Init;
            }
            catch (Exception) { }
        }

        public override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GRAPHNOTIFY)
            {
                if (mediaEvt != null)
                {
                    OnGraphNotify();
                }
                return;
            }
            base.WndProc(ref m);
        }

        public override bool Ended
        {
            get { return m_state == PlayState.Ended; }
        }

        private void OnGraphNotify()
        {
            int p1, p2, hr = 0;
            EventCode code;
            do
            {
                hr = mediaEvt.GetEvent(out code, out p1, out p2, 0);
                if (hr < 0)
                {
                    break;
                }
                hr = mediaEvt.FreeEventParams(code, p1, p2);
                if (code == EventCode.Complete || code == EventCode.ErrorAbort)
                {
                    MovieEnded(false);
                }
            } while (hr == 0);
        }

        public override void Process()
        {
            if (!Playing)
            {
                return;
            }
            if (CurrentPosition >= 10.0)
            {
                if (m_bNotifyPlaying)
                {
                    m_bNotifyPlaying = false;
                    GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_PLAYING_10SEC, 0, 0, 0, 0, 0, null);
                    msg.Label = CurrentFile;
                    GUIWindowManager.SendThreadMessage(msg);
                }
            }
        }

        #region IDisposable Members

        public override void Dispose()
        {
            CloseInterfaces();
        }

        #endregion

    }
}
