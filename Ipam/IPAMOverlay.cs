using System;
using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

public static class IPAMOverlay
{
    private static bool _visible;

    public static bool IsVisible
    {
        get => _visible;
        set
        {
            if (value && !_visible)
            {
                _nextListRefreshTime = 0f;
                _serverSortListDirty = true;
                _switchSortListDirty = true;
            }

            _visible = value;
        }
    }

    private static Rect _windowRect = new(48f, 48f, 1020f, 640f);
    private static Vector2 _scroll = Vector2.zero;
    private static Server _selectedServer;
    private static NetworkSwitch _selectedNetworkSwitch;

    private enum NavSection
    {
        Dashboard = 0,
        Devices = 1,
        IpAddresses = 2,
        Prefixes = 3,
    }

    private static NavSection _navSection = NavSection.Devices;

    private static Server[] _cachedServers = System.Array.Empty<Server>();
    private static NetworkSwitch[] _cachedSwitches = System.Array.Empty<NetworkSwitch>();
    private static float _nextListRefreshTime;
    private static float _cachedContentHeight = 320f;

    private static readonly List<Server> SortedServersBuffer = new();
    private static readonly List<NetworkSwitch> SortedSwitchesBuffer = new();
    private static bool _serverSortListDirty = true;
    private static bool _switchSortListDirty = true;
    private static int _serverSortColumn;
    private static bool _serverSortAscending = true;
    private static int _switchSortColumn;
    private static bool _switchSortAscending = true;

    private const float ListRefreshInterval = 0.35f;

    // Layout (NetBox-style shell)
    private const float ToolbarH = 46f;
    private const float SidebarW = 208f;
    private const float DetailPanelH = 212f;
    private const float TableRowH = 30f;
    private const float SectionTitleH = 22f;
    private const float TableHeaderH = 26f;
    private const float CardPad = 14f;

    /// <summary>Editable IP as four octets — GUI.TextField breaks under IL2CPP (TextEditor unstripping).</summary>
    private static int _oct0 = 192, _oct1 = 168, _oct2 = 1, _oct3 = 10;

    private static Texture2D _texBackdrop;
    private static Texture2D _texSidebar;
    private static Texture2D _texToolbar;
    private static Texture2D _texPageBg;
    private static Texture2D _texCard;
    private static Texture2D _texTableHeader;
    private static Texture2D _texRowA;
    private static Texture2D _texRowB;
    private static Texture2D _texRowHover;
    private static Texture2D _texNavActive;
    private static Texture2D _texPrimaryBtn;
    private static Texture2D _texPrimaryBtnHover;
    private static Texture2D _texMutedBtn;
    private static Texture2D _texMutedBtnHover;
    private static Texture2D _texNavBtnHover;
    private static Texture2D _texModalDim;
    private static bool _texturesReady;

    private static GUIStyle _stModalBlocker;
    private static GUIStyle _stWindowTitle;
    private static GUIStyle _stToolbarTitle;
    private static GUIStyle _stToolbarSub;
    private static GUIStyle _stBadgeOn;
    private static GUIStyle _stBadgeOff;
    private static GUIStyle _stNavItemActive;
    private static GUIStyle _stNavHint;
    private static GUIStyle _stBreadcrumb;
    private static GUIStyle _stSectionTitle;
    private static GUIStyle _stTableHeaderText;
    private static GUIStyle _stHeaderSortBtn;
    private static GUIStyle _stTableCell;
    private static GUIStyle _stNavBtn;
    private static GUIStyle _stMuted;
    private static GUIStyle _stHint;
    private static GUIStyle _stError;
    private static GUIStyle _stFormLabel;
    private static GUIStyle _stOctetVal;
    private static GUIStyle _stPrimaryBtn;
    private static GUIStyle _stMutedBtn;
    private static bool _stylesReady;

    /// <summary>IL2CPP stubs omit <c>RectOffset(l,r,t,b)</c> and <c>new GUIStyle(other)</c>; build via property setters.</summary>
    private static RectOffset Ro(int l, int r, int t, int b)
    {
        var o = new RectOffset();
        o.left = l;
        o.right = r;
        o.top = t;
        o.bottom = b;
        return o;
    }

    /// <summary>
    /// <see cref="GUI.Window"/> may run the window function several times per mouse release (layout/repaint).
    /// <see cref="GUI.Button"/> can then return true multiple times in one frame — dedupe per control key.
    /// </summary>
    private static int _imguiButtonDedupeFrame = -1;
    private static int _imguiButtonDedupeKey = -1;

    private static bool ImguiButtonOnce(Rect r, string text, int dedupeKey, GUIStyle style = null)
    {
        var pressed = style != null ? GUI.Button(r, text, style) : GUI.Button(r, text);
        if (!pressed)
        {
            return false;
        }

        var f = Time.frameCount;
        if (f == _imguiButtonDedupeFrame && dedupeKey == _imguiButtonDedupeKey)
        {
            return false;
        }

        _imguiButtonDedupeFrame = f;
        _imguiButtonDedupeKey = dedupeKey;
        return true;
    }

    /// <summary>
    /// Single-step +/- for octets. Does not use <see cref="GUI.Button"/> — inside <see cref="GUI.Window"/> that helper
    /// can report the same release multiple times per click; <see cref="Event.GetTypeForControl"/> fires once per control.
    /// </summary>
    private static bool OctetStepButton(Rect r, string label, int controlHint)
    {
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, r);
        var e = Event.current;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != id)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                e.Use();
                if (r.Contains(e.mousePosition))
                {
                    return true;
                }

                break;
            case EventType.Repaint:
                if (_stMutedBtn != null)
                {
                    _stMutedBtn.Draw(r, new GUIContent(label), id);
                }
                else
                {
                    GUI.skin.button.Draw(r, new GUIContent(label), id);
                }

                break;
        }

        return false;
    }

    /// <summary>Name / customer / role / IPv4 / status — matches all inventory tables.</summary>
    private static void GetTableColumnWidths(float cardWidth, out float c0, out float c1, out float c2, out float c3, out float c4)
    {
        c0 = cardWidth * 0.24f;
        c1 = cardWidth * 0.22f;
        c2 = cardWidth * 0.10f;
        c3 = cardWidth * 0.20f;
        c4 = cardWidth - c0 - c1 - c2 - c3;
    }

    /// <summary>Stable per Unity object so row order can change (sort) without IMGUI control ID drift.</summary>
    private static int StableRowHint(int section, UnityEngine.Object obj, int uniqueIfNull = 0)
    {
        if (obj != null)
        {
            return HashCode.Combine(section, obj.GetInstanceID());
        }

        return HashCode.Combine(section, unchecked((int)0x9E637E00), uniqueIfNull);
    }

    /// <summary>One IMGUI control per row; columns drawn on Repaint to align with headers.</summary>
    private static bool TableDataRowClick(
        Rect rowRect,
        int controlHint,
        bool altStripe,
        string col1,
        string col2,
        string col3,
        string col4,
        string col5,
        float cardWidth)
    {
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, rowRect);
        var e = Event.current;
        var bgBase = altStripe ? _texRowB : _texRowA;
        GetTableColumnWidths(cardWidth, out var w0, out var w1, out var w2, out var w3, out var w4);

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && rowRect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != id)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                e.Use();
                if (rowRect.Contains(e.mousePosition))
                {
                    return true;
                }

                break;
            case EventType.Repaint:
                var hover = rowRect.Contains(e.mousePosition);
                var bg = hover || GUIUtility.hotControl == id ? _texRowHover : bgBase;
                GUI.DrawTexture(rowRect, bg);
                var x0 = rowRect.x;
                var ry = rowRect.y;
                var rh = rowRect.height;
                DrawTableCellText(new Rect(x0, ry, w0, rh), col1);
                DrawTableCellText(new Rect(x0 + w0, ry, w1, rh), col2);
                DrawTableCellText(new Rect(x0 + w0 + w1, ry, w2, rh), col3);
                DrawTableCellText(new Rect(x0 + w0 + w1 + w2, ry, w3, rh), col4);
                DrawTableCellText(new Rect(x0 + w0 + w1 + w2 + w3, ry, w4, rh), col5);
                break;
        }

        return false;
    }

    private static void DrawTableCellText(Rect r, string text)
    {
        if (Event.current.type != EventType.Repaint || _stTableCell == null)
        {
            return;
        }

        _stTableCell.Draw(r, new GUIContent(text), false, false, false, false);
    }

    private static void EnsureSortedServers()
    {
        if (!_serverSortListDirty)
        {
            return;
        }

        _serverSortListDirty = false;
        SortedServersBuffer.Clear();
        foreach (var s in _cachedServers)
        {
            SortedServersBuffer.Add(s);
        }

        SortedServersBuffer.Sort(CompareServersForSort);
    }

    private static void EnsureSortedSwitches()
    {
        if (!_switchSortListDirty)
        {
            return;
        }

        _switchSortListDirty = false;
        SortedSwitchesBuffer.Clear();
        foreach (var sw in _cachedSwitches)
        {
            SortedSwitchesBuffer.Add(sw);
        }

        SortedSwitchesBuffer.Sort(CompareSwitchesForSort);
    }

    private static int CompareServersForSort(Server a, Server b)
    {
        if (a == null && b == null)
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        int cmp = _serverSortColumn switch
        {
            0 => string.Compare(a.name ?? "", b.name ?? "", StringComparison.OrdinalIgnoreCase),
            1 => string.Compare(
                GameSubnetHelper.GetCustomerDisplayName(a),
                GameSubnetHelper.GetCustomerDisplayName(b),
                StringComparison.OrdinalIgnoreCase),
            2 => 0,
            3 => IpSortKey(DHCPManager.GetServerIP(a)).CompareTo(IpSortKey(DHCPManager.GetServerIP(b))),
            4 => ServerHasAssignedIpRank(a).CompareTo(ServerHasAssignedIpRank(b)),
            _ => 0,
        };

        if (cmp == 0)
        {
            cmp = string.Compare(a.name ?? "", b.name ?? "", StringComparison.OrdinalIgnoreCase);
        }

        return _serverSortAscending ? cmp : -cmp;
    }

    private static int ServerHasAssignedIpRank(Server s)
    {
        var ip = DHCPManager.GetServerIP(s);
        return !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0" ? 1 : 0;
    }

    private static ulong IpSortKey(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
        {
            return 0;
        }

        var p = ip.Trim().Split('.');
        if (p.Length != 4)
        {
            return 0;
        }

        ulong v = 0;
        for (var i = 0; i < 4; i++)
        {
            if (!uint.TryParse(p[i], out var o) || o > 255)
            {
                return 0;
            }

            v = (v << 8) | o;
        }

        return v;
    }

    private static int CompareSwitchesForSort(NetworkSwitch a, NetworkSwitch b)
    {
        if (a == null && b == null)
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        int cmp = _switchSortColumn switch
        {
            0 => string.Compare(a.name ?? "", b.name ?? "", StringComparison.OrdinalIgnoreCase),
            1 => 0,
            2 => 0,
            3 => 0,
            4 => 0,
            _ => 0,
        };

        if (cmp == 0)
        {
            cmp = string.Compare(a.name ?? "", b.name ?? "", StringComparison.OrdinalIgnoreCase);
        }

        return _switchSortAscending ? cmp : -cmp;
    }

    private static void DrawSortableTableHeader(
        Rect r,
        ref int sortColumn,
        ref bool sortAscending,
        string h0,
        string h1,
        string h2,
        string h3,
        string h4,
        int dedupeBase,
        bool markServerSortDirtyOnClick)
    {
        GUI.DrawTexture(r, _texTableHeader);
        GetTableColumnWidths(r.width, out var c0, out var c1, out var c2, out var c3, out var c4);
        var x = r.x;
        var labels = new[] { h0, h1, h2, h3, h4 };
        var widths = new[] { c0, c1, c2, c3, c4 };
        for (var i = 0; i < 5; i++)
        {
            var lab = labels[i];
            if (sortColumn == i)
            {
                lab += sortAscending ? " ▲" : " ▼";
            }

            var cell = new Rect(x, r.y, widths[i], r.height);
            if (ImguiButtonOnce(cell, lab, dedupeBase + i, _stHeaderSortBtn))
            {
                if (sortColumn == i)
                {
                    sortAscending = !sortAscending;
                }
                else
                {
                    sortColumn = i;
                    sortAscending = true;
                }

                if (markServerSortDirtyOnClick)
                {
                    _serverSortListDirty = true;
                }
                else
                {
                    _switchSortListDirty = true;
                }
            }

            x += widths[i];
        }
    }

    public static void TickDeviceListCache()
    {
        if (!IsVisible)
        {
            return;
        }

        var t = Time.realtimeSinceStartup;
        if (t < _nextListRefreshTime)
        {
            return;
        }

        _nextListRefreshTime = t + ListRefreshInterval;
        GameSubnetHelper.RefreshSceneCaches();
        _cachedSwitches = FilterAlive(UnityEngine.Object.FindObjectsOfType<NetworkSwitch>());
        _cachedServers = FilterAlive(UnityEngine.Object.FindObjectsOfType<Server>());
        _serverSortListDirty = true;
        _switchSortListDirty = true;
        RecomputeContentHeight();
    }

    public static void InvalidateDeviceCache()
    {
        _nextListRefreshTime = 0f;
        GameSubnetHelper.InvalidateSceneCaches();
        _serverSortListDirty = true;
        _switchSortListDirty = true;
    }

    public static void Draw()
    {
        if (!IsVisible)
        {
            return;
        }

        EnsureTextures();
        EnsureStyles();

        var oldDepth = GUI.depth;
        // Prefer drawing after other IMGUI in the frame; depth helps automatic layout stacks.
        GUI.depth = 32000;

        var oldBg = GUI.backgroundColor;
        var oldContent = GUI.contentColor;

        // Full-screen IMGUI control: absorbs pointer events for IMGUI stacks. Do not disable
        // UnityEngine.EventSystems.EventSystem here — Data Center's UI_SelectedBorder.Update null-refs when it is off.
        // Drawn before the window so the window (drawn later) still receives hits inside its rect.
        var fullScreen = new Rect(0f, 0f, Screen.width, Screen.height);
        GUI.Box(fullScreen, string.Empty, _stModalBlocker);

        GUI.DrawTexture(_windowRect, _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);

        GUI.backgroundColor = Color.white;
        GUI.contentColor = new Color(0.92f, 0.94f, 0.96f, 1f);

        _windowRect = GUI.Window(9001, _windowRect, (GUI.WindowFunction)DrawWindow, " ");

        GUI.backgroundColor = oldBg;
        GUI.contentColor = oldContent;
        GUI.depth = oldDepth;
    }

    private static void EnsureTextures()
    {
        if (_texturesReady)
        {
            return;
        }

        _texBackdrop = MakeTexture(10, 12, 16, 252);
        _texSidebar = MakeTexture(24, 30, 40, 255);
        _texToolbar = MakeTexture(28, 34, 44, 255);
        _texPageBg = MakeTexture(20, 24, 32, 255);
        _texCard = MakeTexture(30, 36, 46, 255);
        _texTableHeader = MakeTexture(40, 48, 60, 255);
        _texRowA = MakeTexture(34, 40, 52, 255);
        _texRowB = MakeTexture(38, 45, 58, 255);
        _texRowHover = MakeTexture(52, 62, 78, 255);
        _texNavActive = MakeTexture(0, 122, 111, 255);
        _texPrimaryBtn = MakeTexture(0, 133, 120, 255);
        _texPrimaryBtnHover = MakeTexture(0, 152, 136, 255);
        _texMutedBtn = MakeTexture(48, 55, 68, 255);
        _texMutedBtnHover = MakeTexture(58, 66, 82, 255);
        _texNavBtnHover = MakeTexture(38, 46, 60, 255);
        _texModalDim = MakeTexture(0, 0, 0, 140);
        _texturesReady = true;
    }

    private static void EnsureStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _stModalBlocker = new GUIStyle();
        _stModalBlocker.normal.background = _texModalDim;
        _stModalBlocker.border = Ro(0, 0, 0, 0);

        var lf = GUI.skin.label.font;
        var bf = GUI.skin.button.font;

        _stWindowTitle = new GUIStyle();
        _stWindowTitle.font = lf;
        _stWindowTitle.fontSize = 13;
        _stWindowTitle.fontStyle = FontStyle.Bold;
        _stWindowTitle.alignment = TextAnchor.MiddleLeft;
        _stWindowTitle.padding = Ro(10, 8, 0, 0);
        _stWindowTitle.normal.textColor = new Color32(248, 250, 252, 255);

        _stToolbarTitle = new GUIStyle();
        _stToolbarTitle.font = lf;
        _stToolbarTitle.fontSize = 15;
        _stToolbarTitle.fontStyle = FontStyle.Bold;
        _stToolbarTitle.alignment = TextAnchor.MiddleLeft;
        _stToolbarTitle.normal.textColor = new Color32(236, 240, 247, 255);

        _stToolbarSub = new GUIStyle();
        _stToolbarSub.font = lf;
        _stToolbarSub.fontSize = 11;
        _stToolbarSub.alignment = TextAnchor.MiddleLeft;
        _stToolbarSub.normal.textColor = new Color32(154, 164, 178, 255);

        _stBadgeOn = new GUIStyle();
        _stBadgeOn.font = lf;
        _stBadgeOn.fontSize = 9;
        _stBadgeOn.fontStyle = FontStyle.Bold;
        _stBadgeOn.alignment = TextAnchor.MiddleCenter;
        _stBadgeOn.normal.textColor = new Color32(110, 231, 210, 255);
        _stBadgeOn.normal.background = MakeTexture(12, 56, 52, 255);
        _stBadgeOn.border = Ro(4, 4, 4, 4);

        _stBadgeOff = new GUIStyle();
        _stBadgeOff.font = lf;
        _stBadgeOff.fontSize = 9;
        _stBadgeOff.fontStyle = FontStyle.Bold;
        _stBadgeOff.alignment = TextAnchor.MiddleCenter;
        _stBadgeOff.normal.textColor = new Color32(140, 148, 160, 255);
        _stBadgeOff.normal.background = MakeTexture(45, 50, 60, 255);
        _stBadgeOff.border = Ro(4, 4, 4, 4);

        _stNavItemActive = new GUIStyle();
        _stNavItemActive.font = lf;
        _stNavItemActive.fontSize = 12;
        _stNavItemActive.alignment = TextAnchor.MiddleLeft;
        _stNavItemActive.padding = Ro(16, 8, 0, 0);
        _stNavItemActive.normal.textColor = Color.white;

        _stNavHint = new GUIStyle();
        _stNavHint.font = lf;
        _stNavHint.fontSize = 10;
        _stNavHint.alignment = TextAnchor.UpperLeft;
        _stNavHint.wordWrap = true;
        _stNavHint.padding = Ro(14, 10, 8, 4);
        _stNavHint.normal.textColor = new Color32(148, 163, 184, 255);

        _stBreadcrumb = new GUIStyle();
        _stBreadcrumb.font = lf;
        _stBreadcrumb.fontSize = 11;
        _stBreadcrumb.alignment = TextAnchor.MiddleLeft;
        _stBreadcrumb.normal.textColor = new Color32(140, 152, 168, 255);

        _stSectionTitle = new GUIStyle();
        _stSectionTitle.font = lf;
        _stSectionTitle.fontSize = 12;
        _stSectionTitle.fontStyle = FontStyle.Bold;
        _stSectionTitle.alignment = TextAnchor.MiddleLeft;
        _stSectionTitle.normal.textColor = new Color32(226, 232, 240, 255);

        _stTableHeaderText = new GUIStyle();
        _stTableHeaderText.font = lf;
        _stTableHeaderText.fontSize = 10;
        _stTableHeaderText.fontStyle = FontStyle.Bold;
        _stTableHeaderText.alignment = TextAnchor.MiddleLeft;
        _stTableHeaderText.padding = Ro(12, 8, 0, 0);
        _stTableHeaderText.normal.textColor = new Color32(176, 186, 200, 255);

        _stHeaderSortBtn = new GUIStyle();
        _stHeaderSortBtn.font = lf;
        _stHeaderSortBtn.fontSize = 10;
        _stHeaderSortBtn.fontStyle = FontStyle.Bold;
        _stHeaderSortBtn.alignment = TextAnchor.MiddleLeft;
        _stHeaderSortBtn.padding = Ro(10, 6, 0, 0);
        _stHeaderSortBtn.normal.textColor = new Color32(176, 186, 200, 255);
        _stHeaderSortBtn.hover.textColor = new Color32(220, 228, 240, 255);
        _stHeaderSortBtn.active.textColor = Color.white;
        _stHeaderSortBtn.hover.background = MakeTexture(52, 60, 74, 220);
        _stHeaderSortBtn.border = Ro(2, 2, 2, 2);

        _stTableCell = new GUIStyle();
        _stTableCell.font = lf;
        _stTableCell.fontSize = 12;
        _stTableCell.alignment = TextAnchor.MiddleLeft;
        _stTableCell.padding = Ro(12, 8, 0, 0);
        _stTableCell.clipping = TextClipping.Clip;
        _stTableCell.normal.textColor = new Color32(220, 226, 235, 255);

        _stNavBtn = new GUIStyle();
        _stNavBtn.font = lf;
        _stNavBtn.fontSize = 12;
        _stNavBtn.alignment = TextAnchor.MiddleLeft;
        _stNavBtn.padding = Ro(16, 8, 0, 0);
        _stNavBtn.normal.background = _texSidebar;
        _stNavBtn.hover.background = _texNavBtnHover;
        _stNavBtn.active.background = _texNavBtnHover;
        _stNavBtn.normal.textColor = new Color32(203, 213, 225, 255);
        _stNavBtn.hover.textColor = new Color32(240, 244, 250, 255);
        _stNavBtn.active.textColor = Color.white;
        _stNavBtn.border = Ro(0, 0, 0, 0);

        _stMuted = new GUIStyle();
        _stMuted.font = lf;
        _stMuted.fontSize = 11;
        _stMuted.alignment = TextAnchor.MiddleLeft;
        _stMuted.normal.textColor = new Color32(154, 164, 178, 255);

        _stHint = new GUIStyle();
        _stHint.font = lf;
        _stHint.fontSize = 10;
        _stHint.alignment = TextAnchor.UpperLeft;
        _stHint.wordWrap = true;
        _stHint.normal.textColor = new Color32(130, 170, 255, 255);

        _stError = new GUIStyle();
        _stError.font = lf;
        _stError.fontSize = 10;
        _stError.alignment = TextAnchor.UpperLeft;
        _stError.wordWrap = true;
        _stError.normal.textColor = new Color32(255, 130, 120, 255);

        _stFormLabel = new GUIStyle();
        _stFormLabel.font = lf;
        _stFormLabel.fontSize = 11;
        _stFormLabel.fontStyle = FontStyle.Bold;
        _stFormLabel.alignment = TextAnchor.MiddleLeft;
        _stFormLabel.normal.textColor = new Color32(200, 208, 218, 255);

        _stOctetVal = new GUIStyle();
        _stOctetVal.font = lf;
        _stOctetVal.fontSize = 12;
        _stOctetVal.fontStyle = FontStyle.Bold;
        _stOctetVal.alignment = TextAnchor.MiddleCenter;
        _stOctetVal.normal.textColor = new Color32(240, 242, 248, 255);

        _stPrimaryBtn = new GUIStyle();
        _stPrimaryBtn.font = bf;
        _stPrimaryBtn.fontSize = 11;
        _stPrimaryBtn.fontStyle = FontStyle.Bold;
        _stPrimaryBtn.alignment = TextAnchor.MiddleCenter;
        _stPrimaryBtn.padding = Ro(12, 12, 6, 6);
        _stPrimaryBtn.normal.background = _texPrimaryBtn;
        _stPrimaryBtn.hover.background = _texPrimaryBtnHover;
        _stPrimaryBtn.active.background = MakeTexture(0, 104, 94, 255);
        _stPrimaryBtn.normal.textColor = Color.white;
        _stPrimaryBtn.hover.textColor = Color.white;
        _stPrimaryBtn.active.textColor = Color.white;
        _stPrimaryBtn.border = Ro(3, 3, 3, 3);

        _stMutedBtn = new GUIStyle();
        _stMutedBtn.font = bf;
        _stMutedBtn.fontSize = 11;
        _stMutedBtn.alignment = TextAnchor.MiddleCenter;
        _stMutedBtn.padding = Ro(10, 10, 5, 5);
        _stMutedBtn.normal.background = _texMutedBtn;
        _stMutedBtn.hover.background = _texMutedBtnHover;
        _stMutedBtn.active.background = _texMutedBtnHover;
        _stMutedBtn.normal.textColor = new Color32(230, 234, 240, 255);
        _stMutedBtn.hover.textColor = Color.white;
        _stMutedBtn.active.textColor = Color.white;
        _stMutedBtn.border = Ro(3, 3, 3, 3);

        _stylesReady = true;
    }

    private static Texture2D MakeTexture(byte r, byte g, byte b, byte a)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        tex.SetPixel(0, 0, new Color32(r, g, b, a));
        tex.Apply();
        return tex;
    }

    private static void DrawWindow(int id)
    {
        var w = _windowRect.width;
        var h = _windowRect.height;
        var dhcpUnlocked = LicenseManager.IsDHCPUnlocked;
        var ipamUnlocked = LicenseManager.IsIPAMUnlocked;

        // Custom title strip (Unity window chrome is minimal title "")
        const float titleBarH = 28f;
        GUI.DrawTexture(new Rect(0, 0, w, titleBarH), _texSidebar);
        GUI.Label(new Rect(12, 4, w - 120, titleBarH), "IPAM  ·  Data Center", _stWindowTitle);
        if (GUI.Button(new Rect(w - 86, 4, 78, 22), "Close", _stMutedBtn))
        {
            IsVisible = false;
        }

        var toolbarY = titleBarH;
        GUI.DrawTexture(new Rect(0, toolbarY, w, ToolbarH), _texToolbar);
        GUI.Label(new Rect(16, toolbarY + 6, 220, 22), "Inventory", _stToolbarTitle);
        GUI.Label(new Rect(16, toolbarY + 26, 400, 16), "Live devices · IPv4 assignments", _stToolbarSub);

        // Pack from the right so badges are never covered by Auto-DHCP (fixed w-* offsets used to overlap).
        const float tr = 14f;
        const float ty = 10f;
        const float badgeW = 96f;
        const float g = 8f;
        const float pauseW = 152f;
        const float autoW = 168f;
        var tx = w - tr;
        tx -= badgeW;
        DrawBadge(
            new Rect(tx, toolbarY + ty, badgeW, 24),
            "IPAM",
            ipamUnlocked,
            "IPAM license: device tables, IP editor, and nav views. Status only—not a button. (Ctrl+D toggles debug lock for now.)");
        tx -= g + badgeW;
        DrawBadge(
            new Rect(tx, toolbarY + ty, badgeW, 24),
            "DHCP",
            dhcpUnlocked,
            "DHCP license: bulk assign, per-server DHCP auto, and optional empty-IP auto-fill (see “Fill empty” button). Status only—not a button.");
        tx -= g + autoW;
        if (ImguiButtonOnce(new Rect(tx, toolbarY + ty, autoW, 26), "Auto-DHCP (all servers)", 10, _stPrimaryBtn) && dhcpUnlocked)
        {
            DHCPManager.AssignAllServers();
        }

        const float fillW = 118f;
        tx -= g + fillW;
        var fillOn = DHCPManager.EmptyIpAutoFillEnabled;
        var fillLabel = fillOn ? "Fill empty: ON" : "Fill empty: OFF";
        if (ImguiButtonOnce(new Rect(tx, toolbarY + ty, fillW, 26), fillLabel, 12, _stMutedBtn))
        {
            DHCPManager.EmptyIpAutoFillEnabled = !DHCPManager.EmptyIpAutoFillEnabled;
        }

        const float l3W = 100f;
        tx -= g + l3W;
        var l3On = ReachabilityService.EnforcementEnabled;
        if (ImguiButtonOnce(new Rect(tx, toolbarY + ty, l3W, 26), l3On ? "L3: ON" : "L3: OFF", 14, _stMutedBtn))
        {
            ReachabilityService.EnforcementEnabled = !ReachabilityService.EnforcementEnabled;
            ModDebugLog.Bootstrap();
            ModDebugLog.WriteLine(
                ReachabilityService.EnforcementEnabled
                    ? "IPAM: L3 enforcement ON — AddAppPerformance will be checked against routers/DHCP (see IOPS BLOCKED/ALLOW lines when flow is running)."
                    : "IPAM: L3 enforcement OFF — reachability gate skipped for AddAppPerformance (IOPS ALLOW still logs occasionally when flow runs).");
        }

        tx -= g + pauseW;
        if (ImguiButtonOnce(new Rect(tx, toolbarY + ty, pauseW, 26), DHCPManager.IsFlowPaused ? "Resume flow" : "Pause flow", 11, _stMutedBtn))
        {
            DHCPManager.ToggleFlow();
        }

        var bodyTop = toolbarY + ToolbarH;
        var bodyH = h - bodyTop - DetailPanelH;
        GUI.DrawTexture(new Rect(0, bodyTop, w, bodyH), _texPageBg);

        // Sidebar
        GUI.DrawTexture(new Rect(0, bodyTop, SidebarW, bodyH), _texSidebar);
        GUI.Label(new Rect(12, bodyTop + 10, SidebarW - 16, 16), "NAVIGATION", _stNavHint);
        DrawNavEntry(new Rect(8, bodyTop + 30, SidebarW - 8, 32), NavSection.Dashboard, "Dashboard");
        DrawNavEntry(new Rect(8, bodyTop + 64, SidebarW - 8, 32), NavSection.Devices, "Devices");
        DrawNavEntry(new Rect(8, bodyTop + 98, SidebarW - 8, 32), NavSection.IpAddresses, "IP addresses");
        DrawNavEntry(new Rect(8, bodyTop + 132, SidebarW - 8, 32), NavSection.Prefixes, "Prefixes");
        GUI.Label(
            new Rect(8, bodyTop + bodyH - 72, SidebarW - 12, 64),
            "Tip: pick an IP from the contract usable range (rack keypad). Avoid using the gateway as the host address.",
            _stNavHint);

        var contentX = SidebarW + 10f;
        var contentW = w - contentX - 12f;

        if (!ipamUnlocked)
        {
            GUI.DrawTexture(new Rect(contentX, bodyTop + 8, contentW, bodyH - 16), _texCard);
            GUI.Label(new Rect(contentX + CardPad, bodyTop + 24, contentW - CardPad * 2, 40), "Organization  /  Devices", _stBreadcrumb);
            GUI.Label(
                new Rect(contentX + CardPad, bodyTop + 56, contentW - CardPad * 2, 60),
                "IPAM license not unlocked.\nDebug: Ctrl+D toggles license lock.",
                _stMuted);
            GUI.DragWindow(new Rect(0, 0, w, titleBarH + ToolbarH));
            return;
        }

        var scrollTop = bodyTop + 8f;
        var scrollH = bodyH - 16f;
        var scrollViewRect = new Rect(contentX, scrollTop, contentW, scrollH);
        var innerW = scrollViewRect.width - 20f;

        GUI.DrawTexture(new Rect(contentX + 2, scrollTop + 2, contentW - 4, scrollH - 4), _texCard);

        _scroll = GUI.BeginScrollView(
            scrollViewRect,
            _scroll,
            new Rect(0, 0, innerW, _cachedContentHeight));

        switch (_navSection)
        {
            case NavSection.Dashboard:
                DrawDashboard(innerW);
                break;
            case NavSection.Devices:
                DrawDeviceTables(innerW);
                break;
            case NavSection.IpAddresses:
                DrawIpAddressTable(innerW);
                break;
            default:
                DrawPrefixesPlaceholder(innerW);
                break;
        }

        GUI.EndScrollView();

        var panelTop = h - DetailPanelH;
        GUI.DrawTexture(new Rect(0, panelTop, w, DetailPanelH), _texPageBg);
        if (_selectedServer != null)
        {
            DrawServerDetail();
        }
        else if (_selectedNetworkSwitch != null)
        {
            DrawSwitchDetail();
        }

        GUI.DragWindow(new Rect(0, 0, w, titleBarH + ToolbarH));
    }

    private static void DrawBadge(Rect r, string featureName, bool unlocked, string tooltip)
    {
        var text = unlocked ? $"{featureName}  ON" : $"{featureName}  OFF";
        var st = unlocked ? _stBadgeOn : _stBadgeOff;
        GUI.Label(r, new GUIContent(text, tooltip), st);
    }

    private static void DrawNavEntry(Rect r, NavSection target, string text)
    {
        var active = _navSection == target;
        if (active)
        {
            GUI.DrawTexture(r, _texNavActive);
            GUI.Label(new Rect(r.x + 6, r.y, r.width - 8, r.height), text, _stNavItemActive);
            return;
        }

        if (ImguiButtonOnce(r, text, 300 + (int)target, _stNavBtn))
        {
            _navSection = target;
            _scroll = Vector2.zero;
            RecomputeContentHeight();
        }
    }

    private static void RecomputeContentHeight()
    {
        switch (_navSection)
        {
            case NavSection.Dashboard:
                _cachedContentHeight = 260f;
                return;
            case NavSection.IpAddresses:
            {
                var sv = _cachedServers.Length;
                var y = CardPad + SectionTitleH + 2f + 7f + SectionTitleH + 4f + TableHeaderH + sv * TableRowH + CardPad;
                _cachedContentHeight = Mathf.Max(220f, y);
                return;
            }
            case NavSection.Prefixes:
                _cachedContentHeight = 240f;
                return;
            default:
                break;
        }

        var sw = _cachedSwitches.Length;
        var sv2 = _cachedServers.Length;
        var yd = CardPad;
        yd += SectionTitleH + 2f + 7f;
        yd += SectionTitleH + 4f + TableHeaderH + sw * TableRowH;
        yd += 18f + SectionTitleH + 4f + TableHeaderH + sv2 * TableRowH;
        _cachedContentHeight = Mathf.Max(260f, yd + CardPad);
    }

    private static NetworkSwitch[] FilterAlive(NetworkSwitch[] raw)
    {
        if (raw == null || raw.Length == 0)
        {
            return System.Array.Empty<NetworkSwitch>();
        }

        var list = new List<NetworkSwitch>(raw.Length);
        foreach (var x in raw)
        {
            if (x != null)
            {
                list.Add(x);
            }
        }

        return list.ToArray();
    }

    private static Server[] FilterAlive(Server[] raw)
    {
        if (raw == null || raw.Length == 0)
        {
            return System.Array.Empty<Server>();
        }

        var list = new List<Server>(raw.Length);
        foreach (var x in raw)
        {
            if (x != null)
            {
                list.Add(x);
            }
        }

        return list.ToArray();
    }

    /// <summary>
    /// IMGUI assigns control IDs in call order. Always emit the same control sequence (full-width table rows).
    /// </summary>
    private static void DrawDeviceTables(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  Devices  /  All", _stBreadcrumb);
        y += SectionTitleH + 2f;

        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        // --- Switches card ---
        GUI.Label(new Rect(x0, y, 200, SectionTitleH), "Network switches", _stSectionTitle);
        y += SectionTitleH + 4f;

        DrawSortableTableHeader(
            new Rect(x0, y, cardW, TableHeaderH),
            ref _switchSortColumn,
            ref _switchSortAscending,
            "Name",
            "Customer",
            "Role",
            "Mgmt IPv4",
            "Status",
            600,
            false);
        y += TableHeaderH;

        EnsureSortedSwitches();
        for (var i = 0; i < SortedSwitchesBuffer.Count; i++)
        {
            var sw = SortedSwitchesBuffer[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            var name = sw != null ? Trunc(sw.name, 36) : "(removed)";
            var role = sw != null
                ? (NetworkDeviceClassifier.GetKind(sw) == NetworkDeviceKind.Router ? "Router" : "L2 switch")
                : "—";
            if (TableDataRowClick(r, StableRowHint(1, sw, i), i % 2 == 1, name, "—", role, "—", "Active", cardW))
            {
                _selectedNetworkSwitch = sw;
                _selectedServer = null;
            }

            y += TableRowH;
        }

        y += 18f;

        // --- Servers card ---
        GUI.Label(new Rect(x0, y, 200, SectionTitleH), "Servers", _stSectionTitle);
        y += SectionTitleH + 4f;

        DrawSortableTableHeader(
            new Rect(x0, y, cardW, TableHeaderH),
            ref _serverSortColumn,
            ref _serverSortAscending,
            "Name",
            "Customer",
            "Role",
            "IPv4 address",
            "Status",
            610,
            true);
        y += TableHeaderH;

        EnsureSortedServers();
        for (var i = 0; i < SortedServersBuffer.Count; i++)
        {
            var server = SortedServersBuffer[i];
            var r = new Rect(x0, y, cardW, TableRowH);

            if (server == null)
            {
                TableDataRowClick(r, StableRowHint(2, null, i), i % 2 == 1, "(removed)", "—", "Server", "—", "—", cardW);
                y += TableRowH;
                continue;
            }

            var ip = DHCPManager.GetServerIP(server);
            var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
            var ipCol = string.IsNullOrWhiteSpace(ip) ? "—" : ip;
            var status = hasIp ? "Assigned" : "No address";
            var cust = Trunc(GameSubnetHelper.GetCustomerDisplayName(server), 28);
            if (TableDataRowClick(r, StableRowHint(2, server, i), i % 2 == 1, Trunc(server.name, 36), cust, "Server", ipCol, status, cardW))
            {
                _selectedServer = server;
                _selectedNetworkSwitch = null;
                DHCPManager.ClearLastSetIpError();
                LoadOctetsFromIp(ip);
            }

            y += TableRowH;
        }
    }

    private static void DrawDashboard(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var w = innerW - CardPad * 2f;
        GUI.Label(new Rect(x0, y - 2, w, SectionTitleH), "Organization  /  Dashboard", _stBreadcrumb);
        y += SectionTitleH + 8f;
        GUI.Label(new Rect(x0, y, w, SectionTitleH), "Overview", _stSectionTitle);
        y += SectionTitleH + 6f;
        var sw = _cachedSwitches.Length;
        var sv = _cachedServers.Length;
        GUI.Label(new Rect(x0, y, w, 22), $"Network switches in scene:  {sw}", _stMuted);
        y += 24f;
        GUI.Label(new Rect(x0, y, w, 22), $"Servers in scene:  {sv}", _stMuted);
        y += 30f;
        GUI.Label(
            new Rect(x0, y, w, 72f),
            "Open Devices for full inventory tables. IP addresses shows a flat IPv4 list. Toolbar actions apply to all servers.",
            _stHint);
    }

    private static void DrawIpAddressTable(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  IP addresses", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        GUI.Label(new Rect(x0, y, 220, SectionTitleH), "IPv4 assignments", _stSectionTitle);
        y += SectionTitleH + 4f;

        DrawSortableTableHeader(
            new Rect(x0, y, cardW, TableHeaderH),
            ref _serverSortColumn,
            ref _serverSortAscending,
            "Device",
            "Customer",
            "Role",
            "IPv4 address",
            "Status",
            620,
            true);
        y += TableHeaderH;

        EnsureSortedServers();
        for (var i = 0; i < SortedServersBuffer.Count; i++)
        {
            var server = SortedServersBuffer[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            if (server == null)
            {
                TableDataRowClick(r, StableRowHint(4, null, i), i % 2 == 1, "(removed)", "—", "—", "—", "—", cardW);
                y += TableRowH;
                continue;
            }

            var ip = DHCPManager.GetServerIP(server);
            var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
            var ipCol = string.IsNullOrWhiteSpace(ip) ? "—" : ip;
            var status = hasIp ? "Assigned" : "No address";
            var cust = Trunc(GameSubnetHelper.GetCustomerDisplayName(server), 28);
            if (TableDataRowClick(r, StableRowHint(4, server, i), i % 2 == 1, Trunc(server.name, 36), cust, "Server", ipCol, status, cardW))
            {
                _selectedServer = server;
                _selectedNetworkSwitch = null;
                DHCPManager.ClearLastSetIpError();
                LoadOctetsFromIp(ip);
            }

            y += TableRowH;
        }
    }

    private static void DrawPrefixesPlaceholder(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var w = innerW - CardPad * 2f;
        GUI.Label(new Rect(x0, y - 2, w, SectionTitleH), "Organization  /  Prefixes", _stBreadcrumb);
        y += SectionTitleH + 10f;
        GUI.Label(
            new Rect(x0, y, w, 100f),
            "Prefixes follow customer contracts in the base game. Per-VLAN / per-switch DHCP scopes are planned for a future mod release.",
            _stMuted);
    }

    private static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    private static void DrawServerDetail()
    {
        var w = _windowRect.width;
        var h = _windowRect.height;
        var panelY = h - DetailPanelH;
        GUI.DrawTexture(new Rect(0, panelY, w, 1f), _texTableHeader);

        var px = 16f;
        var py = panelY + 10f;

        var currentIp = _selectedServer != null ? DHCPManager.GetServerIP(_selectedServer) : string.Empty;
        var serverName = _selectedServer != null ? _selectedServer.name : "";
        var customerName = _selectedServer != null ? GameSubnetHelper.GetCustomerDisplayName(_selectedServer) : "—";
        GUI.Label(new Rect(px, py, w - 32, 20), "Edit object · Server", _stSectionTitle);
        py += 22f;
        GUI.Label(new Rect(px, py, w - 32, 18), $"Name   {Trunc(serverName, 80)}", _stMuted);
        py += 18f;
        GUI.Label(
            new Rect(px, py, w - 32, 18),
            $"Customer   {Trunc(customerName, 48)}    │    Primary IPv4   {(string.IsNullOrEmpty(currentIp) ? "—" : currentIp)}",
            _stMuted);
        py += 18f;
        var cidStr = _selectedServer == null ? "—" : _selectedServer.GetCustomerID().ToString();
        var modPriv = "—";
        if (_selectedServer != null && CustomerPrivateSubnetRegistry.TryGetPrivateLanCidrForServer(_selectedServer, out var privCidr))
        {
            modPriv = privCidr;
        }

        GUI.Label(
            new Rect(px, py, w - 32, 18),
            $"Game customerID   {cidStr}    │    Mod private LAN   {modPriv}  (DHCP / reachability use this /24)",
            _stMuted);
        py += 22f;

        GUI.Label(new Rect(px, py + 2, 72, 22), "Address", _stFormLabel);
        float ox = px + 78f;
        var oy = py;
        DrawOctetEditor(ref _oct0, ref ox, oy, 0);
        GUI.Label(new Rect(ox, oy + 2, 10, 22), ".", _stOctetVal);
        ox += 12f;
        DrawOctetEditor(ref _oct1, ref ox, oy, 1);
        GUI.Label(new Rect(ox, oy + 2, 10, 22), ".", _stOctetVal);
        ox += 12f;
        DrawOctetEditor(ref _oct2, ref ox, oy, 2);
        GUI.Label(new Rect(ox, oy + 2, 10, 22), ".", _stOctetVal);
        ox += 12f;
        DrawOctetEditor(ref _oct3, ref ox, oy, 3);

        // Second row: default window width cannot fit octets + buttons on one line (~1450px).
        py += 30f;
        ox = px + 78f;
        var btnY = py;
        if (ImguiButtonOnce(new Rect(ox, btnY, 88, 26), "Apply", 32, _stPrimaryBtn) && _selectedServer != null)
        {
            DHCPManager.SetServerIP(_selectedServer, BuildIpFromOctets());
        }

        ox += 96f;
        if (ImguiButtonOnce(new Rect(ox, btnY, 108, 26), "DHCP auto", 33, _stMutedBtn) && _selectedServer != null)
        {
            if (DHCPManager.AssignDhcpToSingleServer(_selectedServer))
            {
                DHCPManager.ClearLastSetIpError();
                LoadOctetsFromIp(DHCPManager.GetServerIP(_selectedServer));
            }
        }

        ox += 116f;
        if (ImguiButtonOnce(new Rect(ox, btnY, 96, 26), "Clipboard", 34, _stMutedBtn))
        {
            LoadOctetsFromIp(GUIUtility.systemCopyBuffer?.Trim());
        }

        ox += 104f;
        if (ImguiButtonOnce(new Rect(ox, btnY, 92, 26), "Clear", 35, _stMutedBtn) && _selectedServer != null)
        {
            if (DHCPManager.SetServerIP(_selectedServer, "0.0.0.0", suppressAutoAssignOnEmpty: true))
            {
                LoadOctetsFromIp("0.0.0.0");
                DHCPManager.ClearLastSetIpError();
                InvalidateDeviceCache();
            }
        }

        py += 32f;
        GUI.Label(new Rect(px, py, w - px - 24, 36), "Must match usable addresses for this contract (see rack keypad). Do not assign the gateway IP as the host.", _stHint);

        var err = DHCPManager.LastSetIpError;
        if (!string.IsNullOrEmpty(err))
        {
            GUI.Label(new Rect(px, panelY + DetailPanelH - 30, w - px - 24, 28), err, _stError);
        }
    }

    private static void DrawSwitchDetail()
    {
        var w = _windowRect.width;
        var h = _windowRect.height;
        var panelY = h - DetailPanelH;
        GUI.DrawTexture(new Rect(0, panelY, w, 1f), _texTableHeader);

        var px = 16f;
        var py = panelY + 10f;
        var sw = _selectedNetworkSwitch;
        var kind = sw != null ? NetworkDeviceClassifier.GetKind(sw) : NetworkDeviceKind.Layer2Switch;
        var role = kind == NetworkDeviceKind.Router ? "Router (L3)" : "Layer 2 switch";
        var model = sw != null ? NetworkDeviceClassifier.GetModelDisplay(sw) : "";
        var modelLine = string.IsNullOrEmpty(model) ? "" : $"    │    Model   {Trunc(model, 40)}";

        GUI.Label(new Rect(px, py, w - 32, 20), "Edit object · Network device", _stSectionTitle);
        py += 22f;
        GUI.Label(new Rect(px, py, w - 32, 18), $"Name   {Trunc(sw != null ? sw.name : "", 72)}    │    Role   {role}{modelLine}", _stMuted);
        py += 24f;

        var ox = px;
        if (ImguiButtonOnce(new Rect(ox, py, 120, 26), "Open CLI", 40, _stPrimaryBtn) && sw != null)
        {
            DeviceTerminalOverlay.OpenFor(sw);
        }

        ox += 128f;
        if (ImguiButtonOnce(new Rect(ox, py, 96, 26), "Deselect", 41, _stMutedBtn))
        {
            _selectedNetworkSwitch = null;
        }

        py += 34f;
        GUI.Label(
            new Rect(px, py, w - px - 24, 44),
            "CLI: type enable, then configure terminal. Routers: hostname, interface Gi0/n, ip address, ip route. Switches: vlan, interface Fa0/n, switchport mode access, switchport access vlan.",
            _stHint);
    }

    private static void DrawOctetEditor(ref int oct, ref float x, float y, int octetSlot)
    {
        oct = Mathf.Clamp(oct, 0, 255);
        const int hintBase = 0x2E435000;
        var minusHint = hintBase + octetSlot * 4;
        var plusHint = minusHint + 1;

        if (OctetStepButton(new Rect(x, y, 26, 26), "−", minusHint))
        {
            oct = Mathf.Max(0, oct - 1);
        }

        x += 28f;
        GUI.Label(new Rect(x, y + 2, 36, 22), oct.ToString(), _stOctetVal);
        x += 40f;
        if (OctetStepButton(new Rect(x, y, 26, 26), "+", plusHint))
        {
            oct = Mathf.Min(255, oct + 1);
        }

        x += 30f;
    }

    private static void LoadOctetsFromIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            _oct0 = 192;
            _oct1 = 168;
            _oct2 = 1;
            _oct3 = 10;
            return;
        }

        var parts = ip.Trim().Split('.');
        if (parts.Length != 4)
        {
            return;
        }

        if (int.TryParse(parts[0], out var a))
        {
            _oct0 = Mathf.Clamp(a, 0, 255);
        }

        if (int.TryParse(parts[1], out var b))
        {
            _oct1 = Mathf.Clamp(b, 0, 255);
        }

        if (int.TryParse(parts[2], out var c))
        {
            _oct2 = Mathf.Clamp(c, 0, 255);
        }

        if (int.TryParse(parts[3], out var d))
        {
            _oct3 = Mathf.Clamp(d, 0, 255);
        }
    }

    private static string BuildIpFromOctets()
    {
        return $"{Mathf.Clamp(_oct0, 0, 255)}.{Mathf.Clamp(_oct1, 0, 255)}.{Mathf.Clamp(_oct2, 0, 255)}.{Mathf.Clamp(_oct3, 0, 255)}";
    }
}
