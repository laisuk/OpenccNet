using System;
using System.Runtime.CompilerServices;

namespace OpenccNetLib
{
    #region OpenccConfig and OpenccConfigExtensions Region

    /// <summary>
    /// Represents all supported OpenCC conversion configurations.
    /// </summary>
    /// <remarks>
    /// Each configuration defines a directional transformation between
    /// Chinese variants (Simplified, Traditional, Taiwan, Hong Kong),
    /// Seal script, or Japanese Shinjitai/Kyūjitai forms.
    /// </remarks>
    public enum OpenccConfig
    {
        /// <summary>
        /// Simplified Chinese → Traditional Chinese (General Standard).
        /// </summary>
        S2T = 0,

        /// <summary>
        /// Traditional Chinese → Simplified Chinese (General Standard).
        /// </summary>
        T2S = 1,

        /// <summary>
        /// Simplified Chinese → Traditional Chinese (Taiwan Standard).
        /// </summary>
        S2Tw = 2,

        /// <summary>
        /// Traditional Chinese (Taiwan Standard) → Simplified Chinese.
        /// </summary>
        Tw2S = 3,

        /// <summary>
        /// Simplified Chinese → Traditional Chinese (Taiwan Standard, with Taiwan idioms).
        /// </summary>
        S2Twp = 4,

        /// <summary>
        /// Traditional Chinese (Taiwan Standard, with idioms) → Simplified Chinese.
        /// </summary>
        Tw2Sp = 5,

        /// <summary>
        /// Simplified Chinese → Traditional Chinese (Hong Kong Standard).
        /// </summary>
        S2Hk = 6,

        /// <summary>
        /// Traditional Chinese (Hong Kong Standard) → Simplified Chinese.
        /// </summary>
        Hk2S = 7,

        /// <summary>
        /// Traditional Chinese (General Standard) → Traditional Chinese (Taiwan Standard).
        /// </summary>
        T2Tw = 8,

        /// <summary>
        /// Traditional Chinese (General Standard) → Traditional Chinese (Taiwan, with idioms).
        /// </summary>
        T2Twp = 9,

        /// <summary>
        /// Traditional Chinese (Taiwan Standard) → Traditional Chinese (General Standard).
        /// </summary>
        Tw2T = 10,

        /// <summary>
        /// Traditional Chinese (Taiwan, with idioms) → Traditional Chinese (General Standard).
        /// </summary>
        Tw2Tp = 11,

        /// <summary>
        /// Traditional Chinese (General Standard) → Traditional Chinese (Hong Kong Standard).
        /// </summary>
        T2Hk = 12,

        /// <summary>
        /// Traditional Chinese (Hong Kong Standard) → Traditional Chinese (General Standard).
        /// </summary>
        Hk2T = 13,

        /// <summary>
        /// Traditional Japanese Kyujitai → Japanese Shinjitai.
        /// </summary>
        T2Jp = 14,

        /// <summary>
        /// Japanese Shinjitai → Traditional Japanese Kyujitai.
        /// </summary>
        Jp2T = 15,

        /// <summary>
        /// Simplified Chinese → Traditional Chinese (Hong Kong Standard, with Hong Kong phrases).
        /// </summary>
        S2Hkp = 16,

        /// <summary>
        /// Traditional Chinese (Hong Kong Standard, with phrases) → Simplified Chinese.
        /// </summary>
        Hk2Sp = 17,

        /// <summary>
        /// Traditional Chinese (General Standard) → Traditional Chinese (Hong Kong Standard, with Hong Kong phrases).
        /// </summary>
        T2Hkp = 18,

        /// <summary>
        /// Traditional Chinese (Hong Kong Standard, with phrases) → Traditional Chinese (General Standard).
        /// </summary>
        Hk2Tp = 19,

        /// <summary>
        /// Simplified Chinese → Seal script.
        /// </summary>
        S2Seal = 20,

        /// <summary>
        /// Traditional Chinese → Seal script.
        /// </summary>
        T2Seal = 21,

        /// <summary>
        /// Seal script → Simplified Chinese.
        /// </summary>
        Seal2S = 22,

        /// <summary>
        /// Seal script → Traditional Chinese.
        /// </summary>
        Seal2T = 23
    }

    /// <summary>
    /// Provides helpers for converting <see cref="OpenccConfig"/> values to their
    /// canonical lowercase OpenCC configuration names.
    /// </summary>
    // @Since v1.4.1
    public static class OpenccConfigExtensions
    {
        /// <summary>
        /// Converts an <see cref="OpenccConfig"/> value to its canonical OpenCC configuration name
        /// (for example, <c>"s2t"</c>, <c>"s2twp"</c>, or <c>"s2seal"</c>).
        /// </summary>
        /// <param name="config">The OpenCC configuration to convert.</param>
        /// <returns>The canonical lowercase configuration name.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="config"/> is not a defined <see cref="OpenccConfig"/> value.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToCanonicalName(this OpenccConfig config)
        {
            switch (config)
            {
                case OpenccConfig.S2T: return "s2t";
                case OpenccConfig.T2S: return "t2s";
                case OpenccConfig.S2Tw: return "s2tw";
                case OpenccConfig.Tw2S: return "tw2s";
                case OpenccConfig.S2Twp: return "s2twp";
                case OpenccConfig.S2Hkp: return "s2hkp";
                case OpenccConfig.Tw2Sp: return "tw2sp";
                case OpenccConfig.Hk2Sp: return "hk2sp";
                case OpenccConfig.T2Hkp: return "t2hkp";
                case OpenccConfig.Hk2Tp: return "hk2tp";
                case OpenccConfig.S2Hk: return "s2hk";
                case OpenccConfig.Hk2S: return "hk2s";
                case OpenccConfig.T2Tw: return "t2tw";
                case OpenccConfig.T2Twp: return "t2twp";
                case OpenccConfig.Tw2T: return "tw2t";
                case OpenccConfig.Tw2Tp: return "tw2tp";
                case OpenccConfig.T2Hk: return "t2hk";
                case OpenccConfig.Hk2T: return "hk2t";
                case OpenccConfig.T2Jp: return "t2jp";
                case OpenccConfig.Jp2T: return "jp2t";
                case OpenccConfig.S2Seal: return "s2seal";
                case OpenccConfig.T2Seal: return "t2seal";
                case OpenccConfig.Seal2S: return "seal2s";
                case OpenccConfig.Seal2T: return "seal2t";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(config),
                        config,
                        "Invalid OpenCC config");
            }
        }
    }

    #endregion // OpenccConfig and OpenccConfigExtensions Region
}