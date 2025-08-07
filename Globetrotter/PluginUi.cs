using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Globetrotter {
    internal class PluginUi {
        private Plugin Plugin { get; }

        private bool _displaySettings;

        private bool DisplaySettings {
            get => this._displaySettings;
            set => this._displaySettings = value;
        }

        public PluginUi(Plugin plugin) {
            this.Plugin = plugin;
        }

        public void OpenSettings() {
            this.DisplaySettings = true;
        }

        public void Draw() {
            if (!this.DisplaySettings) {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(350, 250), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin("Globetrotter settings", ref this._displaySettings)) {
                ImGui.End();
                return;
            }

            ImGui.TextUnformatted("Use /tmap to open your current treasure map.");

            ImGui.Separator();

            var showOnDecipher = this.Plugin.Config.ShowOnDecipher;
            if (HelpCheckbox("Show on decipher", "Open the map with a flag set after deciphering a map.", ref showOnDecipher)) {
                this.Plugin.Config.ShowOnDecipher = showOnDecipher;
                this.Plugin.Config.Save();
            }

            ImGui.Separator();

            var showOnOpen = this.Plugin.Config.ShowOnOpen;
            if (HelpCheckbox("Show on open", "Open the map with a flag set instead of the normal treasure map window.", ref showOnOpen)) {
                this.Plugin.Config.ShowOnOpen = showOnOpen;
                this.Plugin.Config.Save();
            }

            ImGui.Separator();

            var showOnHover = this.Plugin.Config.ShowOnHover;
            if (HelpCheckbox("Show on hover", "Open the map with a flag set when hovering over a deciphered map.", ref showOnHover)) {
                this.Plugin.Config.ShowOnHover = showOnHover;
                this.Plugin.Config.Save();
            }

            ImGui.End();
        }

        private static bool HelpCheckbox(string label, string help, ref bool isChecked) {
            var ret = ImGui.Checkbox(label, ref isChecked);

            ImGui.TreePush("GlobetrotterConfigUI");
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(help);
            ImGui.PopTextWrapPos();
            ImGui.TreePop();

            return ret;
        }
    }
}
