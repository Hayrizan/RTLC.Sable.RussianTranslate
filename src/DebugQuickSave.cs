using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Core.GameManagerStates;
using GameTemplate.Promises;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnhollowerRuntimeLib;

namespace RTLC.Sable.RussianTranslate;

internal static class DebugQuickSave
{
    private static bool _busy, _restore, _waitingForTitle, _waitingForGameplay;
    private static string _id = "", _snapshot = "";
    private static int _slot;
    private static SaveManager? _manager;
    private static GameplayState? _gameplay;
    private static DebugSaveOverlay? _overlay;
    private static InkStory? _debugStoryProxy;
    private static NpcTravelTarget? _pendingNpcTravel;
    private static float _npcTravelDeadline;
    private static float _nextNpcSearch;
    private const double OperationTimeoutSeconds = 120;
    private static double _operationStart;
    private static long _operation;

    internal static void Install(Harmony harmony)
    {
        Patch(harmony, typeof(GameplayState), "Update", nameof(Update));
        Patch(harmony, typeof(GameplayState), "OnEnter", nameof(GameplayEntered));
        Patch(harmony, typeof(TitleScreenState), "Update", nameof(TitleUpdate));
        harmony.Patch(typeof(SaveManager).GetMethod("Save"), prefix: new HarmonyMethod(typeof(DebugQuickSave).GetMethod(nameof(BeforeSave), BindingFlags.Static | BindingFlags.NonPublic)));
        _overlay = BepInEx.IL2CPP.IL2CPPChainloader.AddUnityComponent<DebugSaveOverlay>();
        harmony.Patch(typeof(Cursor).GetProperty(nameof(Cursor.visible))!.GetSetMethod(), prefix: new HarmonyMethod(typeof(DebugSaveOverlay).GetMethod(nameof(DebugSaveOverlay.KeepCursorVisible), BindingFlags.Static | BindingFlags.NonPublic)));
        harmony.Patch(typeof(Cursor).GetProperty(nameof(Cursor.lockState))!.GetSetMethod(), prefix: new HarmonyMethod(typeof(DebugSaveOverlay).GetMethod(nameof(DebugSaveOverlay.KeepCursorUnlocked), BindingFlags.Static | BindingFlags.NonPublic)));
        Report("Горячие клавиши установлены: F4 — дебаг-меню диалогов, F5 — новое сохранение, F6 — выбор слота.");
    }
    private static void Patch(Harmony h, Type type, string method, string callback) => h.Patch(type.GetMethod(method), postfix: new HarmonyMethod(typeof(DebugQuickSave).GetMethod(callback, BindingFlags.Static | BindingFlags.NonPublic)));
    private static bool BeforeSave(ref IPromise __result) { if (!_restore) return true; __result = Promise.Resolved(); return false; }
    private static void Report(string message) => RussianLanguagePatches.LogDebug("[F5/F6] " + message);
    private static void ResetState(string reason)
    {
        _busy = _restore = _waitingForTitle = _waitingForGameplay = false;
        _snapshot = "";
        _operationStart = 0;
        _operation++;
        if (!string.IsNullOrEmpty(reason)) Report(reason);
    }
    private static long BeginOperation()
    {
        _operationStart = Time.realtimeSinceStartup;
        _busy = true;
        return ++_operation;
    }
    internal static void WatchdogTick()
    {
        if (!_busy || Time.realtimeSinceStartup - _operationStart < OperationTimeoutSeconds) return;
        ResetState("F5/F6: операция отменена по тайм-ауту; блокировка сохранений снята.");
    }
    private static bool IsCurrentOperation(long operation)
    {
        WatchdogTick();
        return _busy && operation == _operation;
    }

    private static void Update(GameplayState __instance)
    {
        _gameplay = __instance;
    }
    internal static void OpenMenu(bool save)
    {
        if (_busy) return;
        try
        {
            if (!GameplayState.IsActive || _gameplay == null || _gameplay.CurrentlyInInkOrTimeline) { Report("Сначала войдите в игру и завершите диалог или кат-сцену."); return; }
            _manager = SingletonAsset.Instance<SaveManager>(); if (_manager == null || !_manager.CanSave || SaveManager.SaveInProgress || SaveManager.AutoSaveInProgress) { Report("Игра сейчас не готова к сохранению или загрузке."); return; }
            _id = _manager.CurrentSaveFileID; _slot = _manager.LastLoadedSaveSlot;
            if (string.IsNullOrEmpty(_id) || Path.GetFileName(_id) != _id || _id.Contains("..")) throw new IOException("Invalid save ID.");
            if (save) DebugSaveOverlay.OpenSaveDialog(_id, SaveSnapshot); else DebugSaveOverlay.OpenLoadDialog(_id, OpenSnapshot);
        }
        catch (Exception e) { Report("Ошибка: " + e); }
    }
    internal static bool CanOpenDebugMenu()
    {
        if (_busy) return false;
        if (!GameplayState.IsActive || _gameplay == null || _gameplay.CurrentlyInInkOrTimeline)
        {
            Report("F4: Для отладочного меню завершите текущий диалог или кат-сцену.");
            return false;
        }
        return true;
    }

    internal static DialogueTarget[] GetDialogueTargets()
    {
        var targets = new List<DialogueTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var story in UnityEngine.Object.FindObjectsOfType<InkStory>())
        {
            if (story == null || story == _debugStoryProxy || story.gameObject == null || !story.gameObject.activeInHierarchy) continue;
            try
            {
                var configuration = story.GetConfiguration();
                var knot = !string.IsNullOrWhiteSpace(story.StartAtPath) ? story.StartAtPath : configuration?.InkKnot;
                if (string.IsNullOrWhiteSpace(knot)) continue;
                knot = knot.Trim();
                var label = BuildDialogueLabel(story, knot);
                if (!seen.Add(knot)) continue;
                targets.Add(new DialogueTarget(story, label, knot));
            }
            catch { }
        }
        foreach (var knot in DebugDialogueCatalog.Knots)
            if (seen.Add(knot)) targets.Add(new DialogueTarget(null, DebugDialogueCatalog.Label(knot) + " — " + knot, knot));
        return targets.OrderBy(target => target.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string BuildDialogueLabel(InkStory story, string knot)
    {
        var owner = story.transform;
        while (owner.parent != null && (owner.name == "InkStory" || owner.name == "Story")) owner = owner.parent;
        var ownerName = owner.name;
        if (ownerName == "InkStory" || ownerName == "Story") ownerName = knot;
        return ownerName + " — " + knot;
    }

    internal sealed class DialogueTarget
    {
        internal readonly InkStory? Story;
        internal readonly string Label;
        internal readonly string Knot;
        internal DialogueTarget(InkStory? story, string label, string knot) { Story = story; Label = label; Knot = knot; }
    }

    internal static void StartDialogue(DialogueTarget target)
    {
        if (!CanOpenDebugMenu()) return;
        try
        {
            Report($"F4: Запуск диалога {target.Label}.");
            var story = target.Story != null && target.Story.gameObject != null &&
                target.Story.gameObject.activeInHierarchy ? target.Story : GetDebugStoryProxy();
            story.StartStory(target.Knot);
        }
        catch (Exception e) { Report("F4: Не удалось запустить диалог: " + e.Message); }
    }

    private static InkStory GetDebugStoryProxy()
    {
        if (_debugStoryProxy != null && _debugStoryProxy.gameObject != null) return _debugStoryProxy;
        var gameObject = new GameObject("RTLC Debug Dialogue");
        gameObject.SetActive(false);
        var story = gameObject.AddComponent<InkStory>();
        story.StoryStart = new UnityEvent();
        story.StoryEnd = new UnityEvent();
        story.StartAtPath = "";
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        gameObject.SetActive(true);
        _debugStoryProxy = story;
        return story;
    }

    internal sealed class FastTravelTarget
    {
        internal readonly QuestDestinationData Destination;
        internal readonly string Label;
        internal FastTravelTarget(QuestDestinationData destination, string label)
        {
            Destination = destination;
            Label = label;
        }
    }

    internal static FastTravelTarget[] GetFastTravelLocations()
    {
        var result = new Dictionary<string, FastTravelTarget>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // The console FastTravelLocations list contains legacy coordinates.
            // The map's QuestDestinationData holds the actual player and bike spawns.
            foreach (var resource in Resources.LoadAll("scenecontainers", Il2CppType.Of<QuestDestinationData>()))
                AddDestination(resource?.TryCast<QuestDestinationData>());
            foreach (var resource in Resources.FindObjectsOfTypeAll(Il2CppType.Of<QuestDestinationData>()))
                AddDestination(resource?.TryCast<QuestDestinationData>());
        }
        catch (Exception e) { Report("F4: Не удалось получить список мест: " + e.Message); }
        return result.Values.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();

        void AddDestination(QuestDestinationData? destination)
        {
            if (destination == null || !destination.IsFastTravelLocation ||
                !ValidSpawn(destination.PlayerFastTravelPosition) ||
                !ValidSpawn(destination.BikeFastTravelPosition)) return;
            var key = destination.LocationNameKey;
            if (string.IsNullOrWhiteSpace(key)) key = destination.name;
            if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key)) return;
            var label = TranslationFiles.Translate("Location_" + key, key);
            result.Add(key, new FastTravelTarget(destination, label));
        }
    }

    private static bool ValidSpawn(Vector3 position) =>
        position != Vector3.zero &&
        !float.IsNaN(position.x) && !float.IsNaN(position.y) && !float.IsNaN(position.z) &&
        !float.IsInfinity(position.x) && !float.IsInfinity(position.y) && !float.IsInfinity(position.z);

    internal static void FastTravel(FastTravelTarget target)
    {
        if (!CanOpenDebugMenu() || target?.Destination == null) return;
        try
        {
            var destination = target.Destination;
            var travel = SingletonAsset.Instance<FastTravelLocations>();
            if (travel == null) throw new InvalidOperationException("Система быстрого перемещения не загружена.");
            Report("F4: Штатное быстрое перемещение в " + target.Label + ".");
            travel.FastTravel(destination.PlayerFastTravelPosition, destination.PlayerFastTravelRotation,
                destination.BikeFastTravelPosition, destination.BikeFastTravelRotation)
                .Catch((Il2CppSystem.Action<Il2CppSystem.Exception>)(e =>
                    Report("F4: Быстрое перемещение завершилось ошибкой: " + e)));
        }
        catch (Exception e) { Report("F4: Не удалось переместиться: " + e.Message); }
    }

    internal sealed class NpcTravelTarget
    {
        internal readonly string Knot;
        internal readonly string Label;
        internal readonly QuestDestinationData? Destination;
        internal NpcTravelTarget(string knot, string label, QuestDestinationData? destination)
        {
            Knot = knot;
            Label = label;
            Destination = destination;
        }
    }

    internal static NpcTravelTarget[] GetNpcTravelTargets()
    {
        var result = new List<NpcTravelTarget>();
        var destinations = new List<QuestDestinationData>();
        var activeKnots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var story in UnityEngine.Object.FindObjectsOfType<InkStory>())
            {
                if (story == null || story == _debugStoryProxy || story.gameObject == null ||
                    !story.gameObject.activeInHierarchy) continue;
                try
                {
                    var knot = !string.IsNullOrWhiteSpace(story.StartAtPath)
                        ? story.StartAtPath : story.GetConfiguration()?.InkKnot;
                    if (!string.IsNullOrWhiteSpace(knot)) activeKnots.Add(knot);
                }
                catch { }
            }
            foreach (var resource in Resources.LoadAll("scenecontainers", Il2CppType.Of<QuestDestinationData>()))
            {
                var data = resource?.TryCast<QuestDestinationData>();
                if (data != null && !data.IsFastTravelLocation) destinations.Add(data);
            }
            foreach (var knot in DebugDialogueCatalog.Knots)
            {
                var key = CompactNpcName(knot.Replace("_conversation", "").Replace("_npc", ""));
                var destination = destinations.FirstOrDefault(data =>
                    CompactNpcName(data.name) == key || CompactNpcName(data.name).StartsWith(key, StringComparison.Ordinal));
                if (activeKnots.Contains(knot) || destination != null)
                    result.Add(new NpcTravelTarget(knot, DebugDialogueCatalog.Label(knot), destination));
            }
        }
        catch (Exception e) { Report("F4: Не удалось собрать точки НПС: " + e.Message); }
        return result.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string CompactNpcName(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static InkStory? FindActiveNpcStory(string knot)
    {
        foreach (var story in UnityEngine.Object.FindObjectsOfType<InkStory>())
        {
            if (story == null || story == _debugStoryProxy || story.gameObject == null ||
                !story.gameObject.activeInHierarchy) continue;
            try
            {
                var actualKnot = !string.IsNullOrWhiteSpace(story.StartAtPath)
                    ? story.StartAtPath : story.GetConfiguration()?.InkKnot;
                if (string.Equals(actualKnot, knot, StringComparison.OrdinalIgnoreCase)) return story;
            }
            catch { }
        }
        return null;
    }

    internal static void TeleportToNpc(NpcTravelTarget target)
    {
        if (!CanOpenDebugMenu()) return;
        try
        {
            var story = FindActiveNpcStory(target.Knot);
            if (story != null) { FastTravelNearNpc(story, target.Label); return; }
            if (target.Destination == null) { Report("F4: Местоположение НПС сейчас неизвестно: " + target.Label); return; }
            var waypoint = GetFastTravelLocations().OrderBy(place =>
                (place.Destination.UnloadedPos - target.Destination.UnloadedPos).sqrMagnitude).FirstOrDefault();
            if (waypoint == null) { Report("F4: Не найдена безопасная точка рядом с " + target.Label); return; }
            _pendingNpcTravel = target;
            _npcTravelDeadline = Time.realtimeSinceStartup + 30f;
            _nextNpcSearch = Time.realtimeSinceStartup + 2f;
            Report("F4: Перемещение в район " + target.Label + " через " + waypoint.Label + ".");
            FastTravel(waypoint);
        }
        catch (Exception e) { _pendingNpcTravel = null; Report("F4: Не удалось переместиться к НПС: " + e.Message); }
    }

    internal static void TickNpcTravel()
    {
        if (_pendingNpcTravel == null || Time.realtimeSinceStartup < _nextNpcSearch) return;
        _nextNpcSearch = Time.realtimeSinceStartup + 1f;
        if (Time.realtimeSinceStartup > _npcTravelDeadline)
        {
            Report("F4: Персонаж не появился в загруженной зоне; возможно, он скрыт текущим этапом задания: " + _pendingNpcTravel.Label);
            _pendingNpcTravel = null;
            return;
        }
        if (!GameplayState.IsActive || _gameplay == null || _gameplay.CurrentlyInInkOrTimeline) return;
        var story = FindActiveNpcStory(_pendingNpcTravel.Knot);
        if (story == null) return;
        var label = _pendingNpcTravel.Label;
        _pendingNpcTravel = null;
        try { FastTravelNearNpc(story, label); }
        catch (Exception e) { Report("F4: Не удалось завершить перемещение к НПС: " + e.Message); }
    }

    private static void FastTravelNearNpc(InkStory story, string label)
    {
        var travel = SingletonAsset.Instance<FastTravelLocations>();
        if (travel == null) throw new InvalidOperationException("Система быстрого перемещения не загружена.");
        var facing = story.transform.forward;
        var player = story.transform.position + facing * 2.5f + Vector3.up * 3f;
        var bike = story.transform.position + facing * 6f + Vector3.up * 3f;
        Report("F4: Перемещение к " + label + ", на 3 м выше позиции НПС.");
        travel.FastTravel(player, story.transform.eulerAngles, bike, story.transform.eulerAngles)
            .Catch((Il2CppSystem.Action<Il2CppSystem.Exception>)(e =>
                Report("F4: Перемещение к НПС завершилось ошибкой: " + e)));
    }
    private static string Root => Path.Combine(Application.persistentDataPath, "RTLC_DebugSaves", _id);
    private static string SavePath => Path.Combine(Application.persistentDataPath, "SaveData", _id);
    private static void ValidateSelection()
    {
        if (!GameplayState.IsActive || _gameplay == null || _gameplay.CurrentlyInInkOrTimeline ||
            _manager == null || !_manager.CanSave || SaveManager.SaveInProgress || SaveManager.AutoSaveInProgress)
            throw new InvalidOperationException("Игра сейчас не готова к сохранению или загрузке.");
        if (_manager.CurrentSaveFileID != _id || _manager.LastLoadedSaveSlot != _slot)
            throw new IOException("Save slot changed.");
    }
    private static void SaveSnapshot(string name)
    {
        if (_busy) return;
        var operation = BeginOperation();
        try
        {
            ValidateSelection();
            Directory.CreateDirectory(Root);
            var source = SavePath;
            var target = Path.Combine(Root, DebugSaveSlotStore.MakeSafeName(name) + ".save");
            _manager!.Save().ThenDo((Il2CppSystem.Action)(() =>
            {
                if (!IsCurrentOperation(operation)) return;
                try
                {
                    File.Copy(source, target + ".tmp", true);
                    if (File.Exists(target)) File.Replace(target + ".tmp", target, null, true);
                    else File.Move(target + ".tmp", target);
                    ResetState("F5: слот сохранён.");
                }
                catch (Exception e) { ResetState("Ошибка: " + e.Message); }
            })).Catch((Il2CppSystem.Action<Il2CppSystem.Exception>)(e =>
            {
                if (IsCurrentOperation(operation)) ResetState("Ошибка: " + e);
            }));
        }
        catch (Exception e) { if (IsCurrentOperation(operation)) ResetState("Ошибка: " + e.Message); }
    }
    private static void OpenSnapshot(string name)
    {
        if (_busy) return;
        var operation = BeginOperation();
        try
        {
            ValidateSelection();
            _snapshot = Path.Combine(Root, DebugSaveSlotStore.MakeSafeName(name) + ".save");
            if (!File.Exists(_snapshot)) { ResetState("Слот не найден."); return; }
            _restore = _waitingForTitle = true;
            _gameplay!.HandleQuitToTitleMessage(new Messages.UI.QuitToTitle());
            if (IsCurrentOperation(operation)) Report("F6: загрузка слота.");
        }
        catch (Exception e) { if (IsCurrentOperation(operation)) ResetState("Ошибка: " + e.Message); }
    }
    private static void TitleUpdate(TitleScreenState __instance)
    {
        WatchdogTick();
        if (!_waitingForTitle) { DebugSaveOverlay.CloseMenu(); return; }
        var operation = _operation;
        try
        {
            DebugSaveOverlay.CloseMenu();
            if (__instance.initializing || __instance._saveManager == null) return;
            _waitingForTitle = false;
            if (__instance._saveManager.GetSaveData(_slot).ID != _id) throw new IOException("Save slot changed.");
            var target = SavePath;
            File.Copy(_snapshot, target + ".rtlc-tmp", true);
            if (!IsCurrentOperation(operation)) return;
            File.Replace(target + ".rtlc-tmp", target, target + ".rtlc-before-load", true);
            if (!IsCurrentOperation(operation)) return;
            _waitingForGameplay = true;
            __instance.StartGameInSlot(_slot);
            Report("F6: слот восстановлен.");
        }
        catch (Exception e) { if (IsCurrentOperation(operation)) ResetState("Ошибка: " + e.Message); }
    }
    private static void GameplayEntered()
    {
        WatchdogTick();
        if (_waitingForGameplay) ResetState("F6: игровой процесс восстановлен.");
    }
}

internal sealed class DebugSaveOverlay : MonoBehaviour
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private static DebugSaveOverlay? _instance;
    private bool _saveOpen, _loadOpen, _debugMenuOpen, _f4, _f5, _f6, _escape, _focusInput;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLock;
    private float _previousTimeScale;
    private string _input = "";
    private string[] _slots = Array.Empty<string>();
    private DebugQuickSave.FastTravelTarget[] _fastTravelLocations = Array.Empty<DebugQuickSave.FastTravelTarget>();
    private DebugQuickSave.NpcTravelTarget[] _npcTravelTargets = Array.Empty<DebugQuickSave.NpcTravelTarget>();
    private DebugQuickSave.DialogueTarget[] _dialogueTargets = Array.Empty<DebugQuickSave.DialogueTarget>();
    private int _debugTab;
    private Action<string>? _save, _load;
    private Vector2 _scroll;
    private GUI.WindowFunction? _draw;
    private static bool _lastCursorVisible = true;
    private Rect _window;
    internal static bool IsOpen => _instance != null && (_instance._saveOpen || _instance._loadOpen || _instance._debugMenuOpen);

    public DebugSaveOverlay(IntPtr ptr) : base(ptr) { _instance = this; }

    // Poll in this persistent component, including while the world is paused.
    private void Update()
    {
        RuntimeTextAudit.Tick();
        DebugQuickSave.WatchdogTick();
        DebugQuickSave.TickNpcTravel();
        bool f4 = Application.isFocused && (GetAsyncKeyState(0x73) & 0x8000) != 0;
        bool f5 = Application.isFocused && (GetAsyncKeyState(0x74) & 0x8000) != 0;
        bool f6 = Application.isFocused && (GetAsyncKeyState(0x75) & 0x8000) != 0;
        bool escape = Application.isFocused && (GetAsyncKeyState(0x1B) & 0x8000) != 0;
        bool debugPressed = f4 && !_f4;
        bool savePressed = f5 && !_f5, loadPressed = f6 && !_f6, cancelPressed = escape && !_escape;
        _f4 = f4; _f5 = f5; _f6 = f6; _escape = escape;
        if (IsOpen)
        {
            if (debugPressed || savePressed || loadPressed || cancelPressed) CloseMenu();
        }
        else if (debugPressed)
        {
            OpenDebugMenu();
        }
        else if (savePressed || loadPressed) DebugQuickSave.OpenMenu(savePressed);
    }

    // This game's generated Cursor binding has no visibility getter.
    // Remember the game's setter requests while the overlay is closed.
    internal static void KeepCursorVisible(ref bool value) { if (IsOpen) value = true; else _lastCursorVisible = value; }
    internal static void KeepCursorUnlocked(ref CursorLockMode value) { if (IsOpen) value = CursorLockMode.None; }

    private void Begin(bool save)
    {
        _previousCursorVisible = _lastCursorVisible;
        _previousCursorLock = Cursor.lockState;
        _previousTimeScale = Time.timeScale;
        _saveOpen = save; _loadOpen = !save;
        _window = new Rect(Math.Max(0, Screen.width / 2 - 220), Math.Max(0, Screen.height / 2 - 150), 440, save ? 190 : 300);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0;
    }

    private void BeginDebugMenu()
    {
        _previousCursorVisible = _lastCursorVisible;
        _previousCursorLock = Cursor.lockState;
        _previousTimeScale = Time.timeScale;
        _saveOpen = _loadOpen = false;
        _debugMenuOpen = true;
        _window = new Rect(Math.Max(0, Screen.width / 2 - 360), Math.Max(0, Screen.height / 2 - 290), 720, 580);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0;
    }

    private static void OpenDebugMenu()
    {
        if (_instance == null || !DebugQuickSave.CanOpenDebugMenu()) return;
        var overlay = _instance;
        overlay._dialogueTargets = DebugQuickSave.GetDialogueTargets();
        overlay._fastTravelLocations = DebugQuickSave.GetFastTravelLocations();
        overlay._npcTravelTargets = DebugQuickSave.GetNpcTravelTargets();
        overlay._debugTab = 0;
        overlay.BeginDebugMenu();
    }

    internal static void CloseMenu()
    {
        if (!IsOpen) return;
        var window = _instance!;
        window._saveOpen = window._loadOpen = window._debugMenuOpen = false;
        Cursor.lockState = window._previousCursorLock;
        Cursor.visible = window._previousCursorVisible;
        Time.timeScale = window._previousTimeScale;
        window._save = window._load = null;
    }

    private void OnDisable() { CloseMenu(); }

    internal static void OpenSaveDialog(string id, Action<string> save)
    {
        if (_instance == null) return;
        _instance._input = ""; _instance._save = save; _instance._focusInput = true;
        _instance.Begin(true);
    }

    internal static void OpenLoadDialog(string id, Action<string> load)
    {
        if (_instance == null) return;
        _instance._slots = DebugSaveSlotStore.List(Path.Combine(Application.persistentDataPath, "RTLC_DebugSaves", id));
        _instance._load = load; _instance._scroll = Vector2.zero;
        _instance.Begin(false);
    }

    private void OnGUI()
    {
        if (!IsOpen) return;
        _draw ??= (GUI.WindowFunction)(id => Draw(id));
        var title = _debugMenuOpen ? "Отладка Sable — F4 / Esc: закрыть" : _saveOpen ? "Сохранить снимок" : "Загрузить снимок";
        _window = GUI.ModalWindow(918273, _window, _draw, title, GUI.skin.window);
    }
    private void Draw(int id)
    {
        GUILayout.BeginVertical();
        if (_debugMenuOpen)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_debugTab == 0 ? "[Диалоги персонажей]" : "Диалоги персонажей")) _debugTab = 0;
            if (GUILayout.Button(_debugTab == 1 ? "[Телепортация]" : "Телепортация")) _debugTab = 1;
            if (GUILayout.Button(_debugTab == 2 ? "[К персонажам]" : "К персонажам")) _debugTab = 2;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            if (_debugTab == 0)
            {
                GUILayout.Label("Диалоги загруженных персонажей и каталог диалогов всей игры.");
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(440));
                if (_dialogueTargets.Length == 0) GUILayout.Label("Диалоги не найдены.");
                foreach (var target in _dialogueTargets)
                {
                    if (!GUILayout.Button(target.Label, GUILayout.Height(32))) continue;
                    CloseMenu();
                    DebugQuickSave.StartDialogue(target);
                    break;
                }
                GUILayout.EndScrollView();
            }
            else if (_debugTab == 1)
            {
                GUILayout.Label("Выберите место из доступных игровых точек быстрого перемещения.");
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(440));
                if (_fastTravelLocations.Length == 0) GUILayout.Label("Безопасные игровые точки перемещения не найдены.");
                foreach (var location in _fastTravelLocations)
                {
                    if (!GUILayout.Button(location.Label, GUILayout.Height(32))) continue;
                    CloseMenu();
                    DebugQuickSave.FastTravel(location);
                    break;
                }
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("К загруженному НПС — сразу; к остальным — через ближайшую игровую точку.");
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(440));
                if (_npcTravelTargets.Length == 0) GUILayout.Label("Доступные точки персонажей не найдены.");
                foreach (var target in _npcTravelTargets)
                {
                    if (!GUILayout.Button(target.Label, GUILayout.Height(32))) continue;
                    CloseMenu();
                    DebugQuickSave.TeleportToNpc(target);
                    break;
                }
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("Закрыть (F4 / Esc)")) CloseMenu();
        }
        else if (_saveOpen)
        {
            GUILayout.Label("Название слота:");
            GUI.SetNextControlName("SlotName");
            _input = GUILayout.TextField(_input);
            if (_focusInput && Event.current.type == EventType.Repaint) { GUI.FocusControl("SlotName"); _focusInput = false; }
            if (GUILayout.Button("Сохранить") && _input.Trim().Length > 0)
            {
                var callback = _save; var name = _input;
                CloseMenu(); callback?.Invoke(name);
            }
        }
        else
        {
            if (_slots.Length == 0) GUILayout.Label("Сохранений пока нет.");
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var slot in _slots)
                if (GUILayout.Button(slot)) { var callback = _load; CloseMenu(); callback?.Invoke(slot); break; }
            GUILayout.EndScrollView();
        }
        if (GUILayout.Button("Отмена (Esc / F5 / F6)")) CloseMenu();
        GUILayout.EndVertical(); GUI.DragWindow();
    }
}
