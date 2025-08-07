using Dalamud.Hooking;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace Globetrotter {
    internal sealed class TreasureMaps : IDisposable {
        private const uint TreasureMapsCode = 0x54;

        private static Dictionary<uint, uint>? _mapToRow;

        private Dictionary<uint, uint> MapToRow {
            get {
                if (_mapToRow != null) {
                    return _mapToRow;
                }

                var mapToRow = new Dictionary<uint, uint>();

                foreach (var rank in this.Plugin.DataManager.GetExcelSheet<TreasureHuntRank>()) {
                    var unopened = rank.ItemName.Value;
                    if (unopened.RowId == 0) {
                        continue;
                    }

                    EventItem? opened;
                    // FIXME: remove this try/catch when lumina is fixed
                    try {
                        opened = rank.KeyItemName.Value;
                    } catch (NullReferenceException) {
                        opened = null;
                    }

                    if (opened == null) {
                        continue;
                    }

                    mapToRow[opened.Value.RowId] = rank.RowId;
                }

                _mapToRow = mapToRow;

                return _mapToRow;
            }
        }

        private Plugin Plugin { get; }
        private TreasureMapPacket? _lastMap;

        private delegate char HandleActorControlSelfDelegate(long a1, long a2, IntPtr dataPtr);

        private delegate IntPtr ShowTreasureMapDelegate(IntPtr manager, ushort rowId, ushort subRowId, byte a4);

        private readonly Hook<HandleActorControlSelfDelegate> _acsHook;
        private readonly Hook<ShowTreasureMapDelegate> _showMapHook;

        public TreasureMaps(Plugin plugin) {
            this.Plugin = plugin;

            var acsPtr = this.Plugin.SigScanner.ScanText("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 30 33 FF 48 8B D9");
            this._acsHook = this.Plugin.GameInteropProvider.HookFromAddress<HandleActorControlSelfDelegate>(acsPtr, this.OnACS);
            this._acsHook.Enable();

            var showMapPtr = this.Plugin.SigScanner.ScanText("E8 ?? ?? ?? ?? 44 0F B7 C0 45 33 C9 0F B7 D3");
            this._showMapHook = this.Plugin.GameInteropProvider.HookFromAddress<ShowTreasureMapDelegate>(showMapPtr, this.OnShowMap);
            this._showMapHook.Enable();
        }

        public void Dispose() {
            this._acsHook.Dispose();
            this._showMapHook.Dispose();
        }

        public void OnHover(object? sender, ulong id) {
            if (!this.Plugin.Config.ShowOnHover || this._lastMap == null || this._lastMap.EventItemId != id) {
                return;
            }

            this.OpenMapLocation();
        }

        private IntPtr OnShowMap(IntPtr manager, ushort rowId, ushort subRowId, byte a4) {
            try {
                if (!this.OnShowMapInner(rowId, subRowId)) {
                    return IntPtr.Zero;
                }
            } catch (Exception ex) {
                Plugin.Log.Error(ex, "Exception on show map");
            }

            return this._showMapHook.Original(manager, rowId, subRowId, a4);
        }

        private bool OnShowMapInner(ushort rowId, ushort subRowId) {
            if (this._lastMap == null) {
                try {
                    var eventItemId = this.MapToRow.First(entry => entry.Value == rowId);
                    this._lastMap = new TreasureMapPacket(eventItemId.Key, subRowId, false);
                } catch (InvalidOperationException) {
                    // no-op
                }
            }

            if (!this.Plugin.Config.ShowOnOpen && (!this.Plugin.Config.ShowOnDecipher || this._lastMap?.JustOpened != true)) {
                return true;
            }

            this.OpenMapLocation();
            return false;
        }

        private char OnACS(long a1, long a2, IntPtr dataPtr) {
            try {
                this.OnACSInner(dataPtr);
            } catch (Exception ex) {
                Plugin.Log.Error(ex, "Exception on ACS");
            }

            return this._acsHook.Original(a1, a2, dataPtr);
        }

        private void OnACSInner(IntPtr dataPtr) {
            var packet = ParsePacket(dataPtr);
            if (packet == null) {
                return;
            }

            this._lastMap = packet;
        }

        public void OpenMapLocation() {
            var packet = this._lastMap;

            if (packet == null) {
                return;
            }

            if (!this.MapToRow.TryGetValue(packet.EventItemId, out var rowId)) {
                return;
            }

            var spot = this.Plugin.DataManager.GetSubrowExcelSheet<TreasureSpot>().GetSubrowOrDefault(rowId, (ushort) packet.SubRowId);

            var loc = spot?.Location.Value;
            var map = loc?.Map.Value;
            var terr = map?.TerritoryType.Value;

            if (terr == null) {
                return;
            }

            var x = ToMapCoordinate(loc!.Value.X, map!.Value.SizeFactor);
            var y = ToMapCoordinate(loc.Value.Z, map.Value.SizeFactor);
            var mapLink = new MapLinkPayload(
                terr.Value.RowId,
                map.Value.RowId,
                ConvertMapCoordinateToRawPosition(x, map.Value.SizeFactor),
                ConvertMapCoordinateToRawPosition(y, map.Value.SizeFactor)
            );

            this.Plugin.GameGui.OpenMapWithMapLink(mapLink);

            if (this._lastMap != null) {
                this._lastMap.JustOpened = false;
            }
        }

        private static TreasureMapPacket? ParsePacket(IntPtr dataPtr) {
            uint category = Marshal.ReadByte(dataPtr);
            if (category != TreasureMapsCode) {
                return null;
            }

            dataPtr += 4; // skip padding
            var param1 = (uint) Marshal.ReadInt32(dataPtr);
            dataPtr += 4;
            var param2 = (uint) Marshal.ReadInt32(dataPtr);
            dataPtr += 4;
            var param3 = (uint) Marshal.ReadInt32(dataPtr);

            var eventItemId = param1;
            var subRowId = param2;
            var justOpened = param3 == 1;

            return new TreasureMapPacket(eventItemId, subRowId, justOpened);
        }

        private static int ConvertMapCoordinateToRawPosition(float pos, float scale) {
            var c = scale / 100.0f;

            var scaledPos = (((pos - 1.0f) * c / 41.0f * 2048.0f) - 1024.0f) / c;
            scaledPos *= 1000.0f;

            return (int) scaledPos;
        }

        private static float ToMapCoordinate(float val, float scale) {
            var c = scale / 100f;

            val *= c;
            return (41f / c * ((val + 1024f) / 2048f)) + 1;
        }
    }

    internal class TreasureMapPacket {
        public uint EventItemId { get; }
        public uint SubRowId { get; }
        public bool JustOpened { get; set; }

        public TreasureMapPacket(uint eventItemId, uint subRowId, bool justOpened) {
            this.EventItemId = eventItemId;
            this.SubRowId = subRowId;
            this.JustOpened = justOpened;
        }
    }
}
