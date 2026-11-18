using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Tactical Voice Announcer System (Tingkat 1 & Tingkat 2):
    /// - Mengelola narasi taktis militer untuk Round Flow & Outcomes (Tingkat 1) dan Bomb C4 Operations (Tingkat 2).
    /// - Dilengkapi antrean pintar (Smart Priority Queue) agar suara tidak bertabrakan.
    /// - Dilengkapi efek radio headset squelch / click di awal dan akhir transmisi suara.
    /// </summary>
    public class FPSTacticalAnnouncer : MonoBehaviour
    {
        private static FPSTacticalAnnouncer _instance;
        public static FPSTacticalAnnouncer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<FPSTacticalAnnouncer>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("FPSTacticalAnnouncer");
                        _instance = go.AddComponent<FPSTacticalAnnouncer>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsureAnnouncerExists()
        {
            var _ = Instance;
        }

        [Header("Tingkat 1: Round Flow Audio Clips")]
        [SerializeField] private AudioClip roundOneClip;
        [SerializeField] private AudioClip roundTwoClip;
        [SerializeField] private AudioClip roundThreeClip;
        [SerializeField] private AudioClip roundFourClip;
        [SerializeField] private AudioClip roundFiveClip;
        [SerializeField] private AudioClip roundGenericClip;
        [SerializeField] private AudioClip matchPointClip;
        [SerializeField] private AudioClip finalRoundClip;
        [SerializeField] private AudioClip ctWinClip;
        [SerializeField] private AudioClip tWinClip;
        [SerializeField] private AudioClip matchVictoryClip;
        [SerializeField] private AudioClip matchDefeatClip;

        [Header("Tingkat 2: Bomb Operations Audio Clips")]
        [SerializeField] private AudioClip bombPlantedClip;
        [SerializeField] private AudioClip bombDefusedClip;
        [SerializeField] private AudioClip tenSecondsClip;
        [SerializeField] private AudioClip targetDestroyedClip;

        // Audio & State
        private AudioSource _audioSource;
        private AudioClip _radioClickClip;
        private readonly Queue<AudioClip> _voiceQueue = new Queue<AudioClip>();
        private Coroutine _queueRoutine;
        private bool _tenSecondsTriggered = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D Full Stereo Audio
            _audioSource.priority = 0;      // Prioritas Tertinggi di Sound Engine

            _radioClickClip = GenerateRadioClick();
            LoadAudioClips();
        }

        private void OnEnable()
        {
            TacticalRoundManager.OnRoundStateChanged += HandleRoundStateChanged;
            PlantedC4.OnAnyC4Planted += HandleC4Planted;
            PlantedC4.OnAnyC4Defused += HandleC4Defused;
            PlantedC4.OnAnyC4Exploded += HandleC4Exploded;
        }

        private void OnDisable()
        {
            TacticalRoundManager.OnRoundStateChanged -= HandleRoundStateChanged;
            PlantedC4.OnAnyC4Planted -= HandleC4Planted;
            PlantedC4.OnAnyC4Defused -= HandleC4Defused;
            PlantedC4.OnAnyC4Exploded -= HandleC4Exploded;
        }

        private void Update()
        {
            // Deteksi peringatan darurat: 10 detik tersisa sebelum ledakan C4
            if (PlantedC4.ActiveC4 != null && !_tenSecondsTriggered)
            {
                if (PlantedC4.ActiveC4.RemainingTime <= 10.0f && PlantedC4.ActiveC4.RemainingTime > 1.0f)
                {
                    _tenSecondsTriggered = true;
                    PlayVoice(tenSecondsClip);
                }
            }
        }

        private void LoadAudioClips()
        {
#if UNITY_EDITOR
            string basePath = "Assets/Sound/Announcer/";
            if (roundOneClip == null) roundOneClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "round_one.mp3");
            if (roundTwoClip == null) roundTwoClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "round_two.mp3");
            if (roundThreeClip == null) roundThreeClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "round_three.mp3");
            if (roundFourClip == null) roundFourClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "round_four.mp3");
            if (roundFiveClip == null) roundFiveClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "round_five.mp3");
            if (roundGenericClip == null) roundGenericClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "round_generic.mp3");
            if (matchPointClip == null) matchPointClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "match_point.mp3");
            if (finalRoundClip == null) finalRoundClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "final_round.mp3");
            if (ctWinClip == null) ctWinClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "ct_win.mp3");
            if (tWinClip == null) tWinClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "t_win.mp3");
            if (matchVictoryClip == null) matchVictoryClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "match_victory.mp3");
            if (matchDefeatClip == null) matchDefeatClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "match_defeat.mp3");

            if (bombPlantedClip == null) bombPlantedClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "bomb_planted.mp3");
            if (bombDefusedClip == null) bombDefusedClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "bomb_defused.mp3");
            if (tenSecondsClip == null) tenSecondsClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "ten_seconds.mp3");
            if (targetDestroyedClip == null) targetDestroyedClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "target_destroyed.mp3");
#endif
        }

        #region Event Handlers

        private void HandleRoundStateChanged(RoundState newState)
        {
            TacticalRoundManager rm = TacticalRoundManager.Instance;
            if (rm == null) return;

            if (newState == RoundState.FreezeTime)
            {
                _tenSecondsTriggered = false;
            }
            else if (newState == RoundState.RoundActive)
            {
                // Evaluasi Round Start
                AnnounceRoundStart(rm);
            }
            else if (newState == RoundState.RoundEnd)
            {
                // Evaluasi Round Winner
                AnnounceRoundWinner(rm);
            }
            else if (newState == RoundState.MatchOver)
            {
                // Evaluasi Match Winner
                AnnounceMatchOver(rm);
            }
        }

        private void AnnounceRoundStart(TacticalRoundManager rm)
        {
            int targetWins = rm.TargetRoundWins;
            bool isMatchPointCT = rm.PlayerScore == targetWins - 1;
            bool isMatchPointT = rm.BotScore == targetWins - 1;

            if (isMatchPointCT && isMatchPointT)
            {
                // Skor seri di ambang kemenangan (Final Round)
                PlayVoice(finalRoundClip);
            }
            else if (isMatchPointCT || isMatchPointT)
            {
                // Salah satu tim berada di Match Point
                PlayVoice(matchPointClip);
            }
            else
            {
                // Ronde reguler
                switch (rm.CurrentRound)
                {
                    case 1: PlayVoice(roundOneClip); break;
                    case 2: PlayVoice(roundTwoClip); break;
                    case 3: PlayVoice(roundThreeClip); break;
                    case 4: PlayVoice(roundFourClip); break;
                    case 5: PlayVoice(roundFiveClip); break;
                    default: PlayVoice(roundGenericClip); break;
                }
            }
        }

        private void AnnounceRoundWinner(TacticalRoundManager rm)
        {
            // Beri sedikit jeda 0.4 detik agar suara tembakan/ledakan terakhir selesai bergaung
            if (rm.DidPlayerWinLastRound)
            {
                PlayVoice(ctWinClip, 0.4f);
            }
            else
            {
                PlayVoice(tWinClip, 0.4f);
            }
        }

        private void AnnounceMatchOver(TacticalRoundManager rm)
        {
            bool playerWon = rm.PlayerScore >= rm.TargetRoundWins;
            if (playerWon)
            {
                PlayVoice(matchVictoryClip, 0.5f);
            }
            else
            {
                PlayVoice(matchDefeatClip, 0.5f);
            }
        }

        private void HandleC4Planted(PlantedC4 c4, string site)
        {
            _tenSecondsTriggered = false;
            PlayVoice(bombPlantedClip);
        }

        private void HandleC4Defused(PlantedC4 c4)
        {
            PlayVoice(bombDefusedClip);
        }

        private void HandleC4Exploded(PlantedC4 c4)
        {
            PlayVoice(targetDestroyedClip, 0.6f);
        }

        #endregion

        #region Voice Playback & Smart Queue

        /// <summary>
        /// Memasukkan klip suara narator ke dalam antrean pintar dan memutarnya berurutan tanpa bertabrakan.
        /// </summary>
        public void PlayVoice(AudioClip clip, float delay = 0f)
        {
            if (clip == null) return;
            StartCoroutine(EnqueueWithDelay(clip, delay));
        }

        private IEnumerator EnqueueWithDelay(AudioClip clip, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            _voiceQueue.Enqueue(clip);

            if (_queueRoutine == null)
            {
                _queueRoutine = StartCoroutine(ProcessVoiceQueue());
            }
        }

        private IEnumerator ProcessVoiceQueue()
        {
            while (_voiceQueue.Count > 0)
            {
                AudioClip clip = _voiceQueue.Dequeue();
                if (clip != null && _audioSource != null)
                {
                    // 1. Radio Chirp / Click Awal Transmisi
                    if (_radioClickClip != null)
                    {
                        _audioSource.pitch = Random.Range(0.96f, 1.04f);
                        _audioSource.PlayOneShot(_radioClickClip, 0.4f);
                        yield return new WaitForSeconds(0.04f);
                    }

                    // 2. Mainkan Suara Announcer
                    _audioSource.pitch = 1.0f;
                    _audioSource.PlayOneShot(clip, 1.0f);

                    // Tunggu hingga klip suara selesai diputar
                    yield return new WaitForSeconds(clip.length + 0.08f);

                    // 3. Radio Chirp / Click Akhir Transmisi
                    if (_radioClickClip != null)
                    {
                        _audioSource.pitch = Random.Range(0.92f, 0.98f);
                        _audioSource.PlayOneShot(_radioClickClip, 0.35f);
                        yield return new WaitForSeconds(0.12f);
                    }
                }
            }

            _queueRoutine = null;
        }

        private static AudioClip GenerateRadioClick()
        {
            int sampleRate = 44100;
            int samples = Mathf.CeilToInt(sampleRate * 0.035f); // 35ms burst
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float burst = Mathf.Sin(2f * Mathf.PI * 1600f * (i / (float)sampleRate)) * (1f - t);
                float noise = (Random.value * 2f - 1f) * 0.18f * (1f - t);
                data[i] = (burst + noise) * 0.35f;
            }
            AudioClip clip = AudioClip.Create("RadioClick_Proc", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        #endregion
    }
}
