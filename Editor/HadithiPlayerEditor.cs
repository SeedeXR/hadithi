using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SeedeXR.Hadithi.EditorTools
{
    /// <summary>
    /// The friendly Inspector for HadithiPlayer:
    ///   - beats as a reorderable list, collapsed to one readable line each,
    ///   - only the fields relevant to the chosen end condition,
    ///   - one timeline slot per language, labeled with the language name and code,
    ///   - inline validation warnings,
    ///   - a "start from this beat" button in Play Mode.
    /// </summary>
    [CustomEditor(typeof(HadithiPlayer))]
    public class HadithiPlayerEditor : Editor
    {
        SerializedProperty _playOnStart, _loopWhenFinished, _controllerAlwaysAdvances;
        SerializedProperty _languages, _currentLanguage, _beats, _ambience;
        SerializedProperty _onAnyBeatStart, _onStoryFinished;
        ReorderableList _beatList;

        // Properties, not static readonly fields: reading these EditorGUIUtility values
        // during static initialization throws (style catalog access is illegal in that
        // context), which kills the whole editor type. Lazy access is always safe.
        static float Line => EditorGUIUtility.singleLineHeight;
        static float Pad => EditorGUIUtility.standardVerticalSpacing;

        void OnEnable()
        {
            _playOnStart = serializedObject.FindProperty(nameof(HadithiPlayer.playOnStart));
            _loopWhenFinished = serializedObject.FindProperty(nameof(HadithiPlayer.loopWhenFinished));
            _controllerAlwaysAdvances = serializedObject.FindProperty(nameof(HadithiPlayer.controllerAlwaysAdvances));
            _languages = serializedObject.FindProperty(nameof(HadithiPlayer.languages));
            _currentLanguage = serializedObject.FindProperty(nameof(HadithiPlayer.currentLanguage));
            _beats = serializedObject.FindProperty(nameof(HadithiPlayer.beats));
            _ambience = serializedObject.FindProperty(nameof(HadithiPlayer.ambience));
            _onAnyBeatStart = serializedObject.FindProperty(nameof(HadithiPlayer.onAnyBeatStart));
            _onStoryFinished = serializedObject.FindProperty(nameof(HadithiPlayer.onStoryFinished));

            _beatList = new ReorderableList(serializedObject, _beats, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "Beats  (one runs at a time, top to bottom)", EditorStyles.boldLabel),
                drawElementCallback = DrawBeat,
                elementHeightCallback = BeatHeight,
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Section headers come from the runtime fields' [Header] decorators;
            // drawing labels here as well would double them.
            EditorGUILayout.PropertyField(_playOnStart);
            EditorGUILayout.PropertyField(_loopWhenFinished);
            EditorGUILayout.PropertyField(_controllerAlwaysAdvances);

            EditorGUILayout.PropertyField(_languages, true);
            DrawLanguagePopup();
            SyncTimelineSlots();

            EditorGUILayout.Space();
            _beatList.DoLayoutList();

            EditorGUILayout.PropertyField(_ambience, true);

            EditorGUILayout.PropertyField(_onAnyBeatStart);
            EditorGUILayout.PropertyField(_onStoryFinished);

            serializedObject.ApplyModifiedProperties();
        }

        // Draws Current Language as a dropdown of language names instead of a raw index.
        void DrawLanguagePopup()
        {
            var names = new List<string>();
            for (int i = 0; i < _languages.arraySize; i++)
            {
                SerializedProperty lang = _languages.GetArrayElementAtIndex(i);
                string n = lang.FindPropertyRelative("name").stringValue;
                string c = lang.FindPropertyRelative("code").stringValue;
                names.Add(string.IsNullOrEmpty(c) ? n : $"{n} ({c})");
            }
            if (names.Count == 0)
            {
                EditorGUILayout.HelpBox("Add at least one language. The first is the default.", MessageType.Warning);
                return;
            }
            int current = Mathf.Clamp(_currentLanguage.intValue, 0, names.Count - 1);
            int chosen = EditorGUILayout.Popup("Current Language", current, names.ToArray());
            _currentLanguage.intValue = chosen;
        }

        // Every beat keeps one timeline slot per language. Grow only: removing a
        // language never silently discards an assigned timeline.
        void SyncTimelineSlots()
        {
            int langCount = Mathf.Max(1, _languages.arraySize);
            for (int i = 0; i < _beats.arraySize; i++)
            {
                SerializedProperty timelines =
                    _beats.GetArrayElementAtIndex(i).FindPropertyRelative("timelines");
                while (timelines.arraySize < langCount) timelines.arraySize++;
            }
        }

        // ── Beat element ────────────────────────────────────────────────────────

        void DrawBeat(Rect rect, int index, bool active, bool focused)
        {
            SerializedProperty beat = _beats.GetArrayElementAtIndex(index);
            SerializedProperty name = beat.FindPropertyRelative("name");
            SerializedProperty endsWhen = beat.FindPropertyRelative("endsWhen");

            rect.y += Pad;
            var header = new Rect(rect.x + 12f, rect.y, rect.width - 12f, Line);
            string summary = $"{index + 1}.  {name.stringValue}      [{ObjectNames.NicifyVariableName(((HadithiPlayer.EndCondition)endsWhen.enumValueIndex).ToString())}]";
            beat.isExpanded = EditorGUI.Foldout(header, beat.isExpanded, summary, true);
            if (!beat.isExpanded) return;

            float y = rect.y + Line + Pad;
            float x = rect.x + 12f;
            float w = rect.width - 12f;

            Field(ref y, x, w, name);
            Field(ref y, x, w, beat.FindPropertyRelative("screen"));

            // One labeled slot per language.
            SerializedProperty timelines = beat.FindPropertyRelative("timelines");
            for (int l = 0; l < _languages.arraySize && l < timelines.arraySize; l++)
            {
                SerializedProperty lang = _languages.GetArrayElementAtIndex(l);
                string label = $"Timeline {lang.FindPropertyRelative("code").stringValue}";
                string tip = lang.FindPropertyRelative("name").stringValue
                             + (l == 0 ? " (default and fallback)" : string.Empty);
                EditorGUI.PropertyField(new Rect(x, y, w, Line),
                    timelines.GetArrayElementAtIndex(l), new GUIContent(label, tip));
                y += Line + Pad;
            }

            Field(ref y, x, w, endsWhen, "Ends When");

            var condition = (HadithiPlayer.EndCondition)endsWhen.enumValueIndex;
            SerializedProperty buttons = beat.FindPropertyRelative("buttons");
            SerializedProperty zone = beat.FindPropertyRelative("zone");
            SerializedProperty seconds = beat.FindPropertyRelative("seconds");

            switch (condition)
            {
                case HadithiPlayer.EndCondition.ButtonClicked:
                    float bh = EditorGUI.GetPropertyHeight(buttons, true);
                    EditorGUI.PropertyField(new Rect(x, y, w, bh), buttons, true);
                    y += bh + Pad;
                    break;
                case HadithiPlayer.EndCondition.PlayerEntersZone:
                    Field(ref y, x, w, zone);
                    Field(ref y, x, w, seconds, "Timeout (0 = forever)");
                    break;
                case HadithiPlayer.EndCondition.WaitSeconds:
                    Field(ref y, x, w, seconds, "Seconds");
                    break;
                case HadithiPlayer.EndCondition.WaitForSignal:
                    Field(ref y, x, w, seconds, "Timeout (0 = forever)");
                    break;
            }

            string problem = Validate(beat, condition);
            if (problem != null)
            {
                EditorGUI.HelpBox(new Rect(x, y, w, Line * 2f), problem, MessageType.Warning);
                y += Line * 2f + Pad;
            }

            // Events behind one foldout to keep beats compact.
            SerializedProperty onStart = beat.FindPropertyRelative("onBeatStart");
            SerializedProperty onEnd = beat.FindPropertyRelative("onBeatEnd");
            onStart.isExpanded = EditorGUI.Foldout(new Rect(x, y, w, Line), onStart.isExpanded, "Events", true);
            y += Line + Pad;
            if (onStart.isExpanded)
            {
                float h1 = EditorGUI.GetPropertyHeight(onStart);
                EditorGUI.PropertyField(new Rect(x, y, w, h1), onStart);
                y += h1 + Pad;
                float h2 = EditorGUI.GetPropertyHeight(onEnd);
                EditorGUI.PropertyField(new Rect(x, y, w, h2), onEnd);
                y += h2 + Pad;
            }

            if (Application.isPlaying)
            {
                if (GUI.Button(new Rect(x, y, 180f, Line), "Start from this beat"))
                {
                    var player = (HadithiPlayer)target;
                    if (!player.IsPlaying) player.Play();
                    player.JumpTo(index);
                }
            }
        }

        float BeatHeight(int index)
        {
            SerializedProperty beat = _beats.GetArrayElementAtIndex(index);
            float h = Line + Pad * 2f;
            if (!beat.isExpanded) return h;

            SerializedProperty endsWhen = beat.FindPropertyRelative("endsWhen");
            var condition = (HadithiPlayer.EndCondition)endsWhen.enumValueIndex;

            h += (Line + Pad) * 2f;                                   // name, screen
            int slots = Mathf.Min(_languages.arraySize,
                beat.FindPropertyRelative("timelines").arraySize);
            h += (Line + Pad) * slots;                                // timeline slots
            h += Line + Pad;                                          // ends when

            switch (condition)
            {
                case HadithiPlayer.EndCondition.ButtonClicked:
                    h += EditorGUI.GetPropertyHeight(beat.FindPropertyRelative("buttons"), true) + Pad;
                    break;
                case HadithiPlayer.EndCondition.PlayerEntersZone:
                    h += (Line + Pad) * 2f;
                    break;
                case HadithiPlayer.EndCondition.WaitSeconds:
                case HadithiPlayer.EndCondition.WaitForSignal:
                    h += Line + Pad;
                    break;
            }

            if (Validate(beat, condition) != null) h += Line * 2f + Pad;

            SerializedProperty onStart = beat.FindPropertyRelative("onBeatStart");
            h += Line + Pad;                                          // events foldout
            if (onStart.isExpanded)
            {
                h += EditorGUI.GetPropertyHeight(onStart) + Pad;
                h += EditorGUI.GetPropertyHeight(beat.FindPropertyRelative("onBeatEnd")) + Pad;
            }

            if (Application.isPlaying) h += Line + Pad;               // start from this beat
            return h + Pad;
        }

        void Field(ref float y, float x, float w, SerializedProperty prop, string label = null)
        {
            var content = label != null ? new GUIContent(label) : null;
            EditorGUI.PropertyField(new Rect(x, y, w, Line), prop, content);
            y += Line + Pad;
        }

        // One inline message per beat, worst problem first. Null when healthy.
        static string Validate(SerializedProperty beat, HadithiPlayer.EndCondition condition)
        {
            switch (condition)
            {
                case HadithiPlayer.EndCondition.PlayerEntersZone:
                    if (beat.FindPropertyRelative("zone").objectReferenceValue == null)
                        return "No Zone assigned: this beat will be skipped at play time.";
                    break;
                case HadithiPlayer.EndCondition.ButtonClicked:
                    SerializedProperty buttons = beat.FindPropertyRelative("buttons");
                    bool any = false;
                    for (int i = 0; i < buttons.arraySize; i++)
                        if (buttons.GetArrayElementAtIndex(i).objectReferenceValue != null) any = true;
                    if (!any)
                        return "No Buttons assigned: the beat can only advance through the controller fallback.";
                    break;
                case HadithiPlayer.EndCondition.TimelineFinishes:
                    SerializedProperty timelines = beat.FindPropertyRelative("timelines");
                    bool anyTimeline = false;
                    for (int i = 0; i < timelines.arraySize; i++)
                        if (timelines.GetArrayElementAtIndex(i).objectReferenceValue != null) anyTimeline = true;
                    if (!anyTimeline)
                        return "Ends on Timeline Finishes but no timeline is assigned: the beat will advance immediately.";
                    break;
                case HadithiPlayer.EndCondition.WaitSeconds:
                    if (beat.FindPropertyRelative("seconds").floatValue <= 0f)
                        return "Seconds is 0: the beat will advance immediately.";
                    break;
            }
            return null;
        }
    }
}
