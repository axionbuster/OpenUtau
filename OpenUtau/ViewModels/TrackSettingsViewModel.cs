using System;
using System.Linq;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class TrackSettingsViewModel : ViewModelBase {
        public UTrack Track { get; private set; }
        public ObservableCollectionExtended<IResampler> Resamplers => resamplers;
        [Reactive] public partial IResampler? Resampler { get; set; }
        [Reactive] public partial bool NeedsResampler { get; set; }
        public ObservableCollectionExtended<IWavtool> Wavtools => wavtools;
        [Reactive] public partial IWavtool? Wavtool { get; set; }
        [Reactive] public partial bool NeedsWavtool { get; set; }
        [Reactive] public partial bool IsNotClassic { get; set; }
        public bool IsVoiSona { get; }
        [Reactive] public partial bool NativePitch { get; set; }
        public Core.VoiSona.VoiSonaSettings VoiSona { get; }
        public string VoiSonaStyles => "Singing style: Normal (the installed Chis-A voices have one style).";

        ObservableCollectionExtended<IResampler> resamplers =
            new ObservableCollectionExtended<IResampler>();
        ObservableCollectionExtended<IWavtool> wavtools =
            new ObservableCollectionExtended<IWavtool>();

        public TrackSettingsViewModel(UTrack track) {
            ToolsManager.Inst.Initialize();
            Track = track;
            IsVoiSona = track.RendererSettings.renderer == Renderers.VOISONA;
            VoiSona = track.RendererSettings.voisona?.ValidatedCopy() ?? new Core.VoiSona.VoiSonaSettings();
            NativePitch = VoiSona.NativePitch;
            if (!string.IsNullOrEmpty(Track.RendererSettings.renderer)) {
                var renderer = Track.RendererSettings.renderer;
                resamplers.AddRange(ToolsManager.Inst.Resamplers);
                string? resamplerName = Track.RendererSettings.resampler;
                if (string.IsNullOrEmpty(resamplerName)) {
                    if (!Preferences.Default.DefaultResamplers.TryGetValue(renderer, out resamplerName)) {
                        resamplerName = string.Empty;
                    }
                }
                Resampler = ToolsManager.Inst.GetResampler(resamplerName);
                wavtools.AddRange(Renderers.GetSupportedWavtools(Resampler));
                string? wavtoolName = Track.RendererSettings.wavtool;
                if (string.IsNullOrEmpty(wavtoolName)) {
                    if (!Preferences.Default.DefaultWavtools.TryGetValue(renderer, out wavtoolName)) {
                        wavtoolName = string.Empty;
                    }
                }
                Wavtool = ToolsManager.Inst.GetWavtool(wavtoolName);
                NeedsResampler = Renderers.CLASSIC == renderer;
                NeedsWavtool = Renderers.CLASSIC == renderer;
                IsNotClassic = Renderers.CLASSIC != renderer && !IsVoiSona;
            }
            this.WhenAnyValue(x => x.Resampler)
                .OfType<IResampler>()
                .Subscribe(resampler => {
                    resampler?.CheckPermissions();
                    var wavtool = Wavtool;
                    wavtools.Clear();
                    wavtools.AddRange(Renderers.GetSupportedWavtools(resampler));
                    if (wavtool != null && wavtools.Contains(wavtool)) {
                        Wavtool = wavtool;
                    } else {
                        Wavtool = wavtools.FirstOrDefault();
                    }
                });
            this.WhenAnyValue(x => x.Wavtool)
                .OfType<IWavtool>()
                .Subscribe(wavtool => {
                    wavtool?.CheckPermissions();
                });
        }

        public void OpenResamplerLocation() {
            OS.OpenFolder(PathManager.Inst.ResamplersPath);
        }

        public void SetDefaultResampler() {
            if (Resampler != null) {
                Preferences.Default.DefaultResamplers[Track.RendererSettings.renderer] = Resampler.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void OpenWavtoolLocation() {
            OS.OpenFolder(PathManager.Inst.WavtoolsPath);
        }

        public void SetDefaultWavtool() {
            if (Wavtool != null) {
                Preferences.Default.DefaultWavtools[Track.RendererSettings.renderer] = Wavtool.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void Finish() {
            if (Renderers.CLASSIC != Track.RendererSettings.renderer && !IsVoiSona) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.setting");
            var settings = Track.RendererSettings.Clone();
            if (IsVoiSona) {
                VoiSona.NativePitch = NativePitch;
                settings.voisona = VoiSona.ValidatedCopy();
            }
            else {
                settings.resampler = Resampler?.ToString() ?? string.Empty;
                settings.wavtool = Wavtool?.ToString() ?? string.Empty;
            }
            DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, Track, settings));
            DocManager.Inst.EndUndoGroup();
        }
    }
}
