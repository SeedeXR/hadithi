using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace SeedeXR.Hadithi
{
    /// <summary>
    /// The Hadithi story engine. Author the whole story in the Inspector:
    ///
    ///   Beats:    an ordered, reorderable list; exactly ONE beat is alive at a time,
    ///             so reading the list top to bottom is reading the story. Each beat
    ///             shows a Screen (optional), plays a Timeline in the active language
    ///             (optional), and says how it ends.
    ///   Ambience: parallel entries that play alongside the story and loop
    ///             (background music, ambient effects); not part of the story queue.
    ///
    /// Custom mechanics integrate through the Wait For Signal seam: a beat's
    /// On Beat Start event fires the mechanic, and the mechanic calls Signal() when
    /// it is finished. See README.md for the full authoring guide.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SeedeXR/Hadithi Player")]
    public class HadithiPlayer : MonoBehaviour
    {
        // ── Authoring types ─────────────────────────────────────────────────────

        public enum EndCondition
        {
            ButtonClicked,
            ControllerPress,
            TimelineFinishes,
            PlayerEntersZone,
            WaitSeconds,
            WaitForSignal,
        }

        [Serializable]
        public class Language
        {
            [Tooltip("Display name, e.g. English.")]
            public string name = "English";

            [Tooltip("Short indicator shown next to timeline slots, e.g. EN.")]
            public string code = "EN";
        }

        [Serializable]
        public class Beat
        {
            [Tooltip("Readable label for this story beat.")]
            public string name = "New Beat";

            [Tooltip("Screen shown while this beat is active, hidden when it ends. Optional.")]
            public GameObject screen;

            [Tooltip("One timeline per language, matched to the Languages list by position. An empty slot falls back to the first language's timeline.")]
            public List<PlayableDirector> timelines = new List<PlayableDirector>();

            [Tooltip("How this beat ends and the story moves on.")]
            public EndCondition endsWhen = EndCondition.ControllerPress;

            [Tooltip("Buttons that end the beat. Used by Button Clicked.")]
            public List<Button> buttons = new List<Button>();

            [Tooltip("Zone the player must enter. Used by Player Enters Zone.")]
            public HadithiZone zone;

            [Tooltip("Wait Seconds: the wait duration. Player Enters Zone / Wait For Signal: a safety timeout, 0 means wait forever.")]
            public float seconds;

            public UnityEvent onBeatStart = new UnityEvent();
            public UnityEvent onBeatEnd = new UnityEvent();
        }

        [Serializable]
        public class AmbienceEntry
        {
            [Tooltip("Readable label for this ambience entry.")]
            public string name = "New Ambience";

            [Tooltip("Audio to play. Optional.")]
            public AudioSource audio;

            [Tooltip("Timeline to play. Optional.")]
            public PlayableDirector timeline;

            [Tooltip("GameObject to activate. Optional.")]
            public GameObject activate;

            [Tooltip("Start this entry when the story starts.")]
            public bool playOnStoryStart = true;

            [Tooltip("Loop the audio / timeline.")]
            public bool loop = true;
        }

        // ── Inspector ───────────────────────────────────────────────────────────

        [Header("Playback")]
        [Tooltip("Start the story automatically on Start().")]
        public bool playOnStart = true;

        [Tooltip("Restart from the first beat after the last one ends.")]
        public bool loopWhenFinished = true;

        [Tooltip("Safety fallback: RequestAdvance() also advances Button Clicked beats, so a broken ray never strands the player.")]
        public bool controllerAlwaysAdvances = true;

        [Header("Languages")]
        [Tooltip("Languages this story supports. Add as many as you want. The FIRST is the default and the fallback for empty timeline slots.")]
        public List<Language> languages = new List<Language> { new Language() };

        [Tooltip("Index into Languages used at play time. Change via SetLanguage().")]
        public int currentLanguage;

        [Header("Story")]
        public List<Beat> beats = new List<Beat>();

        [Header("Ambience (parallel, not part of the story)")]
        public List<AmbienceEntry> ambience = new List<AmbienceEntry>();

        [Header("Global events")]
        [Tooltip("Fired whenever ANY beat starts. Handy for one time wiring, e.g. re arming controller input each beat.")]
        public UnityEvent onAnyBeatStart = new UnityEvent();

        [Tooltip("Fired when the story ends (only when Loop When Finished is off).")]
        public UnityEvent onStoryFinished = new UnityEvent();

        // ── Runtime state ───────────────────────────────────────────────────────

        public int CurrentBeatIndex { get; private set; } = -1;
        public Beat CurrentBeat =>
            CurrentBeatIndex >= 0 && CurrentBeatIndex < beats.Count ? beats[CurrentBeatIndex] : null;
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// Seconds until the current beat's timeout elapses (Wait Seconds duration, or
        /// the optional timeout on zone / signal beats). Negative when the current beat
        /// has no time limit or nothing is playing. Poll this to display a countdown.
        /// </summary>
        public float CurrentBeatRemainingSeconds =>
            IsPlaying && !float.IsPositiveInfinity(_beatDeadline)
                ? Mathf.Max(0f, _beatDeadline - Time.time)
                : -1f;

        bool _forceAdvance;
        bool _signal;
        int _jumpIndex = -1;
        float _beatDeadline = float.PositiveInfinity;
        Coroutine _run;
        readonly List<(Button button, UnityAction action)> _liveButtons =
            new List<(Button, UnityAction)>();
        PlayableDirector _liveDirector;

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Starts the story from the first beat (no effect while playing).</summary>
        public void Play()
        {
            if (IsPlaying) return;
            if (beats == null || beats.Count == 0)
            {
                Debug.LogWarning("[Hadithi] No beats authored; nothing to play.", this);
                return;
            }
            IsPlaying = true;
            HideAllScreens();
            StartAmbience();
            _run = StartCoroutine(Run());
        }

        /// <summary>Stops the story and its ambience, hiding the current screen.</summary>
        public void StopStory()
        {
            if (!IsPlaying) return;
            if (_run != null) StopCoroutine(_run);
            ExitBeat(CurrentBeat);
            CurrentBeatIndex = -1;
            IsPlaying = false;
            StopAmbience();
        }

        /// <summary>Forces the current beat to end now, whatever its end condition.</summary>
        public void Advance() => _forceAdvance = true;

        /// <summary>
        /// Polite advance for controller input: honored when the current beat ends on
        /// Controller Press, or on Button Clicked when the safety fallback is on.
        /// Ignored otherwise, so a press can never skip a zone, wait or signal beat.
        /// </summary>
        public void RequestAdvance()
        {
            Beat b = CurrentBeat;
            if (b == null) return;
            if (b.endsWhen == EndCondition.ControllerPress ||
                (b.endsWhen == EndCondition.ButtonClicked && controllerAlwaysAdvances))
            {
                _forceAdvance = true;
            }
        }

        /// <summary>Completes a Wait For Signal beat.</summary>
        public void Signal() => _signal = true;

        /// <summary>Jumps to a beat by position in the list (0 based).</summary>
        public void JumpTo(int index)
        {
            if (index < 0 || index >= beats.Count)
            {
                Debug.LogWarning($"[Hadithi] JumpTo({index}) is out of range.", this);
                return;
            }
            _jumpIndex = index;
            _forceAdvance = true;
        }

        /// <summary>Jumps to the first beat with this name.</summary>
        public void JumpTo(string beatName)
        {
            for (int i = 0; i < beats.Count; i++)
            {
                if (beats[i].name == beatName) { JumpTo(i); return; }
            }
            Debug.LogWarning($"[Hadithi] JumpTo: no beat named '{beatName}'.", this);
        }

        /// <summary>Switches the active language by position in the Languages list.</summary>
        public void SetLanguage(int index)
        {
            if (index < 0 || index >= languages.Count)
            {
                Debug.LogWarning($"[Hadithi] SetLanguage({index}) is out of range.", this);
                return;
            }
            currentLanguage = index;
        }

        /// <summary>Switches the active language by its name or code (case insensitive).</summary>
        public void SetLanguage(string nameOrCode)
        {
            for (int i = 0; i < languages.Count; i++)
            {
                Language l = languages[i];
                if (string.Equals(l.name, nameOrCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.code, nameOrCode, StringComparison.OrdinalIgnoreCase))
                {
                    currentLanguage = i;
                    return;
                }
            }
            Debug.LogWarning($"[Hadithi] SetLanguage: no language named '{nameOrCode}'.", this);
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────

        void Start()
        {
            if (playOnStart) Play();
        }

        void OnDisable()
        {
            if (IsPlaying) StopStory();
        }

        // ── The story loop ──────────────────────────────────────────────────────

        IEnumerator Run()
        {
            do
            {
                int i = 0;
                while (i < beats.Count)
                {
                    CurrentBeatIndex = i;
                    Beat beat = beats[i];

                    EnterBeat(beat);
                    yield return WaitForBeatEnd(beat);
                    ExitBeat(beat);

                    if (_jumpIndex >= 0)
                    {
                        i = _jumpIndex;
                        _jumpIndex = -1;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
            while (loopWhenFinished);

            CurrentBeatIndex = -1;
            IsPlaying = false;
            onStoryFinished?.Invoke();
        }

        void EnterBeat(Beat beat)
        {
            _forceAdvance = false;
            _signal = false;

            if (beat.screen != null) beat.screen.SetActive(true);

            _liveDirector = TimelineFor(beat);
            if (_liveDirector != null) _liveDirector.Play();

            if (beat.endsWhen == EndCondition.ButtonClicked)
            {
                foreach (Button b in beat.buttons)
                {
                    if (b == null) continue;
                    UnityAction action = Advance;
                    b.onClick.AddListener(action);
                    _liveButtons.Add((b, action));
                }
            }

            beat.onBeatStart?.Invoke();
            onAnyBeatStart?.Invoke();
        }

        void ExitBeat(Beat beat)
        {
            if (beat == null) return;

            foreach ((Button button, UnityAction action) in _liveButtons)
            {
                if (button != null) button.onClick.RemoveListener(action);
            }
            _liveButtons.Clear();

            if (_liveDirector != null)
            {
                _liveDirector.Stop();
                _liveDirector = null;
            }

            if (beat.screen != null) beat.screen.SetActive(false);

            beat.onBeatEnd?.Invoke();
        }

        IEnumerator WaitForBeatEnd(Beat beat)
        {
            switch (beat.endsWhen)
            {
                case EndCondition.ButtonClicked:
                case EndCondition.ControllerPress:
                    // Both resolve through _forceAdvance (buttons call Advance();
                    // controller input routes through RequestAdvance()).
                    return WaitUntilDone(() => false, 0f);

                case EndCondition.TimelineFinishes:
                    if (_liveDirector == null)
                    {
                        Debug.LogWarning($"[Hadithi] Beat '{beat.name}' ends on Timeline Finishes but has no timeline for the active language; advancing immediately.", this);
                        return null;
                    }
                    PlayableDirector d = _liveDirector;
                    return WaitUntilDone(
                        () => d == null || d.state != PlayState.Playing || d.time >= d.duration,
                        0f);

                case EndCondition.PlayerEntersZone:
                    if (beat.zone == null)
                    {
                        Debug.LogError($"[Hadithi] Beat '{beat.name}' ends on Player Enters Zone but has no Zone assigned; advancing immediately so the story is not stranded.", this);
                        return null;
                    }
                    return WaitUntilDone(() => beat.zone.HasEntered, beat.seconds);

                case EndCondition.WaitSeconds:
                    // The wait IS the timeout, so the countdown surfaces through
                    // CurrentBeatRemainingSeconds like every other timed beat.
                    // Epsilon keeps the documented "0 advances immediately" contract.
                    return WaitUntilDone(() => false, Mathf.Max(0.0001f, beat.seconds));

                case EndCondition.WaitForSignal:
                    return WaitUntilDone(() => _signal, beat.seconds);

                default:
                    return null;
            }
        }

        // Waits until the condition holds, Advance()/JumpTo() force it, or the
        // timeout elapses (timeout 0 means no timeout). The deadline is kept in a
        // field so CurrentBeatRemainingSeconds can expose the live countdown.
        IEnumerator WaitUntilDone(Func<bool> done, float timeoutSeconds)
        {
            _beatDeadline = timeoutSeconds > 0f ? Time.time + timeoutSeconds : float.PositiveInfinity;
            while (!_forceAdvance && !done() && Time.time < _beatDeadline)
            {
                yield return null;
            }
            _beatDeadline = float.PositiveInfinity;
        }

        // ── Screens, timelines, ambience ────────────────────────────────────────

        void HideAllScreens()
        {
            foreach (Beat beat in beats)
            {
                if (beat.screen != null) beat.screen.SetActive(false);
            }
        }

        // The active language's timeline, falling back to the first slot when empty.
        PlayableDirector TimelineFor(Beat beat)
        {
            if (beat.timelines == null || beat.timelines.Count == 0) return null;
            if (currentLanguage < beat.timelines.Count && beat.timelines[currentLanguage] != null)
            {
                return beat.timelines[currentLanguage];
            }
            return beat.timelines[0];
        }

        void StartAmbience()
        {
            foreach (AmbienceEntry entry in ambience)
            {
                if (!entry.playOnStoryStart) continue;

                if (entry.audio != null)
                {
                    entry.audio.loop = entry.loop;
                    entry.audio.Play();
                }
                if (entry.timeline != null)
                {
                    entry.timeline.extrapolationMode =
                        entry.loop ? DirectorWrapMode.Loop : entry.timeline.extrapolationMode;
                    entry.timeline.Play();
                }
                if (entry.activate != null) entry.activate.SetActive(true);
            }
        }

        void StopAmbience()
        {
            foreach (AmbienceEntry entry in ambience)
            {
                if (entry.audio != null) entry.audio.Stop();
                if (entry.timeline != null) entry.timeline.Stop();
                if (entry.activate != null) entry.activate.SetActive(false);
            }
        }
    }
}
