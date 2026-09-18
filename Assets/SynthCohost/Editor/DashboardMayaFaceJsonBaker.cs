using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SynthCohost.Runtime.Features.Avatar;
using UnityEditor;
using UnityEngine;

namespace SynthCohost.Editor
{
    /// <summary>
    /// Bakes Maya Advanced Skeleton face-rig JSON into <see cref="DashboardFaceCurveClip"/> assets.
    /// FBX face takes animate joints this Character Creator mesh is not skinned to.
    /// </summary>
    public static class DashboardMayaFaceJsonBaker
    {
        public const string OutputFolder =
            "Assets/SynthCohost/Runtime/Features/Avatar/FaceCurves";

        public const string CatalogPath =
            OutputFolder + "/DashboardFaceCurveCatalog.asset";

        private static readonly string[] FacialJsonBaseNames =
        {
            "46_Smile",
            "47_Laugh",
            "48_cheer",
            "50_Thumbs_Up",
            "51_Shocked",
            "51_Shoked_2",
            "52_sad",
            "57_Yawning",
            "60_Excited_Greeting",
            "Wink",
            "Slow_Blink",
            "Double_Blink",
            "Gasp",
            "Nod",
            "sigh",
            "sleepy",
            "53_Thinking",
            "Listening",
            "33_Head_Tilt",
            "29_Notice_Cursor",
            "32_Wave",
            "35_Observe_User",
            "59_Looking_Bored"
        };

        private static readonly Dictionary<string, string> StateNameOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["59_Looking_Bored"] = "59_looking_bored"
            };

        [MenuItem("Tools/Synth Cohost/Bake Maya Face JSON Curves")]
        public static void BakeAllMenu()
        {
            var count = BakeAll();
            EditorUtility.DisplayDialog(
                "Synth Cohost Face Curves",
                $"Baked {count} face curve clip(s) into {OutputFolder}.",
                "OK");
        }

        public static int BakeAll()
        {
            EnsureFolder(OutputFolder);
            var baked = new List<DashboardFaceCurveClip>();
            for (var i = 0; i < FacialJsonBaseNames.Length; i++)
            {
                var baseName = FacialJsonBaseNames[i];
                var jsonPath = Path.Combine(
                    Application.dataPath,
                    "3D Models",
                    "(.json)-20260911T113348Z-1-001",
                    "(.json)",
                    baseName + ".json");
                if (!File.Exists(jsonPath))
                {
                    Debug.LogWarning($"[SynthCohost/FaceBake] Missing JSON: {baseName}.json");
                    continue;
                }

                if (!StateNameOverrides.TryGetValue(baseName, out var stateName))
                {
                    stateName = baseName;
                }

                EditorUtility.DisplayProgressBar(
                    "Baking face curves",
                    stateName,
                    (float)i / FacialJsonBaseNames.Length);
                try
                {
                    var clip = BakeFile(jsonPath, stateName);
                    if (clip != null)
                    {
                        baked.Add(clip);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SynthCohost/FaceBake] Failed {baseName}: {ex.Message}");
                }
            }

            EditorUtility.ClearProgressBar();

            var catalog = AssetDatabase.LoadAssetAtPath<DashboardFaceCurveCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<DashboardFaceCurveCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetClips(baked.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SynthCohost/FaceBake] Wrote {baked.Count} clips + catalog.");
            return baked.Count;
        }

        internal static DashboardFaceCurveClip BakeFile(string jsonPath, string stateName)
        {
            var text = File.ReadAllText(jsonPath);
            var frameBegin = ReadInt(text, "\"framebegin\"", 1);
            var frameEnd = ReadInt(text, "\"frameend\"", frameBegin);
            var fps = ReadFloat(text, "\"fps\"", 30f);
            if (fps < 1f)
            {
                fps = 30f;
            }

            var duration = Mathf.Max(0.033f, (frameEnd - frameBegin) / fps);
            var sampleCount = Mathf.Max(2, frameEnd - frameBegin + 1);
            var blinkL = new float[sampleCount];
            var blinkR = new float[sampleCount];
            var squintL = new float[sampleCount];
            var squintR = new float[sampleCount];
            var smile = new float[sampleCount];
            var frown = new float[sampleCount];
            var jaw = new float[sampleCount];
            var mouthClose = new float[sampleCount];
            var eyeWideL = new float[sampleCount];
            var eyeWideR = new float[sampleCount];
            var browInner = new float[sampleCount];
            var browDown = new float[sampleCount];

            var happyKeys = ExtractScalarKeys(text, "ctrlEmotions_M__DOT__happy");
            var sadKeys = ExtractScalarKeys(text, "ctrlEmotions_M__DOT__sad");
            var surpriseKeys = ExtractScalarKeys(text, "ctrlEmotions_M__DOT__surprise");
            var fearKeys = ExtractScalarKeys(text, "ctrlEmotions_M__DOT__fear");
            var angryKeys = ExtractScalarKeys(text, "ctrlEmotions_M__DOT__angry");
            var blinkLKeys = Prefer(
                ExtractScalarKeys(text, "upperLid_L__DOT__blink"),
                ExtractScalarKeys(text, "ctrlEye_L__DOT__blink"));
            var blinkRKeys = Prefer(
                ExtractScalarKeys(text, "upperLid_R__DOT__blink"),
                ExtractScalarKeys(text, "ctrlEye_R__DOT__blink"));
            var squintLKeys = ExtractScalarKeys(text, "ctrlEye_L__DOT__squint");
            var squintRKeys = ExtractScalarKeys(text, "ctrlEye_R__DOT__squint");
            var wideLKeys = ExtractScalarKeys(text, "ctrlEye_L__DOT__wide");
            var wideRKeys = ExtractScalarKeys(text, "ctrlEye_R__DOT__wide");
            var jawRotateX = ExtractComponentKeys(text, "Jaw_M__DOT__rotate", 0);
            var cheekL = ExtractComponentKeys(text, "ctrlCheek_L__DOT__translate", 1);
            var cheekR = ExtractComponentKeys(text, "ctrlCheek_R__DOT__translate", 1);
            var mouthCornerLY = ExtractComponentKeys(text, "ctrlMouthCorner_L__DOT__translate", 1);
            var mouthTY = ExtractComponentKeys(text, "ctrlMouth_M__DOT__translate", 1);
            var phonemeAa = ExtractScalarKeys(text, "ctrlPhonemes_M__DOT__Aa");
            var phonemeO = ExtractScalarKeys(text, "ctrlPhonemes_M__DOT__O");
            var phonemeAaa = ExtractScalarKeys(text, "ctrlPhonemes_M__DOT__AAA");
            var browInnerL = ExtractComponentKeys(text, "EyeBrowInner_L__DOT__translate", 1);
            var browInnerR = ExtractComponentKeys(text, "EyeBrowInner_R__DOT__translate", 1);
            var browCenter = ExtractComponentKeys(text, "EyeBrowCenter_M__DOT__translate", 1);
            var jawRest = SampleKeys(jawRotateX, frameBegin);
            var blinkPeak = Mathf.Max(1f, PeakAbs(blinkLKeys), PeakAbs(blinkRKeys));
            var happyPeak = Mathf.Max(0.01f, PeakAbs(happyKeys));

            for (var i = 0; i < sampleCount; i++)
            {
                var frame = frameBegin + i;
                var happy = SampleKeys(happyKeys, frame);
                var sad = SampleKeys(sadKeys, frame);
                var surprise = SampleKeys(surpriseKeys, frame);
                var fear = SampleKeys(fearKeys, frame);
                var angry = SampleKeys(angryKeys, frame);
                var cheek = 0.5f * (SampleKeys(cheekL, frame) + SampleKeys(cheekR, frame));
                var corner = SampleKeys(mouthCornerLY, frame);
                var mouthDrop = Mathf.Abs(SampleKeys(mouthTY, frame));

                var smileWeight = (happy / happyPeak) * (stateName.IndexOf("wink", StringComparison.OrdinalIgnoreCase) >= 0 ? 35f : 75f);
                smile[i] = Mathf.Clamp(
                    smileWeight + Mathf.Max(0f, cheek) * 18f + Mathf.Max(0f, corner) * 120f,
                    0f,
                    100f);
                frown[i] = Mathf.Clamp((sad / 1.66f) * 70f + (angry / 3f) * 20f, 0f, 100f);
                blinkL[i] = Mathf.Clamp((SampleKeys(blinkLKeys, frame) / blinkPeak) * 60f, 0f, 60f);
                blinkR[i] = Mathf.Clamp((SampleKeys(blinkRKeys, frame) / blinkPeak) * 60f, 0f, 60f);
                squintL[i] = Mathf.Clamp(SampleKeys(squintLKeys, frame) * 40f, 0f, 80f);
                squintR[i] = Mathf.Clamp(SampleKeys(squintRKeys, frame) * 40f, 0f, 80f);
                eyeWideL[i] = Mathf.Clamp(
                    SampleKeys(wideLKeys, frame) * 90f + (surprise / 1.66f) * 55f + (fear / 1.66f) * 35f,
                    0f,
                    100f);
                eyeWideR[i] = Mathf.Clamp(
                    SampleKeys(wideRKeys, frame) * 90f + (surprise / 1.66f) * 55f + (fear / 1.66f) * 35f,
                    0f,
                    100f);
                browInner[i] = Mathf.Clamp(
                    (fear / 1.66f) * 55f +
                    (surprise / 1.66f) * 25f +
                    0.5f * (SampleKeys(browInnerL, frame) + SampleKeys(browInnerR, frame)) * 40f +
                    SampleKeys(browCenter, frame) * 20f,
                    0f,
                    100f);
                browDown[i] = Mathf.Clamp((angry / 3f) * 45f + (sad / 1.66f) * 25f, 0f, 100f);
                var jawDelta = Mathf.Abs(SampleKeys(jawRotateX, frame) - jawRest);
                var phonemeJaw =
                    (SampleKeys(phonemeAa, frame) / 5.29f) * 52f +
                    (SampleKeys(phonemeO, frame) / 3.27f) * 40f +
                    (SampleKeys(phonemeAaa, frame) / 1f) * 8f;
                jaw[i] = Mathf.Clamp(Mathf.Max(jawDelta * 2.2f, phonemeJaw, mouthDrop * 90f), 0f, 70f);
                mouthClose[i] = 0f;
            }

            if (stateName.IndexOf("sleepy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    blinkL[i] = Mathf.Max(blinkL[i], 42f);
                    blinkR[i] = Mathf.Max(blinkR[i], 42f);
                }
            }

            // Nod JSON carries leftover happy from the Maya export; keep the face neutral.
            if (stateName.IndexOf("nod", StringComparison.OrdinalIgnoreCase) >= 0 &&
                stateName.IndexOf("smile", StringComparison.OrdinalIgnoreCase) < 0)
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    smile[i] = 0f;
                }
            }

            // Some face JSONs drive Maya joints only. Fill weak channels from the known heuristic pose.
            ApplyHeuristicFallback(stateName, blinkL, blinkR, squintL, squintR, smile, frown, jaw, eyeWideL, eyeWideR, browInner, browDown);

            var assetPath = $"{OutputFolder}/{SanitizeFileName(stateName)}.asset";
            var clip = AssetDatabase.LoadAssetAtPath<DashboardFaceCurveClip>(assetPath);
            if (clip == null)
            {
                clip = ScriptableObject.CreateInstance<DashboardFaceCurveClip>();
                AssetDatabase.CreateAsset(clip, assetPath);
            }

            clip.Configure(
                stateName,
                duration,
                ToCurve(blinkL),
                ToCurve(blinkR),
                ToCurve(squintL),
                ToCurve(squintR),
                ToCurve(smile),
                ToCurve(frown),
                ToCurve(jaw),
                ToCurve(mouthClose),
                ToCurve(eyeWideL),
                ToCurve(eyeWideR),
                ToCurve(browInner),
                ToCurve(browDown));
            EditorUtility.SetDirty(clip);
            return clip;
        }

        internal static AnimationCurve ToCurve(float[] samples)
        {
            var curve = new AnimationCurve();
            if (samples == null || samples.Length == 0)
            {
                curve.AddKey(0f, 0f);
                curve.AddKey(1f, 0f);
                return curve;
            }

            var last = samples.Length - 1;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = last == 0 ? 0f : (float)i / last;
                curve.AddKey(t, samples[i]);
            }

            for (var i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }

            return curve;
        }

        internal static float SampleKeys(List<KeyValuePair<float, float>> keys, float frame)
        {
            if (keys == null || keys.Count == 0)
            {
                return 0f;
            }

            if (frame <= keys[0].Key)
            {
                return keys[0].Value;
            }

            if (frame >= keys[keys.Count - 1].Key)
            {
                return keys[keys.Count - 1].Value;
            }

            for (var i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (frame < a.Key || frame > b.Key)
                {
                    continue;
                }

                if (Mathf.Approximately(a.Key, b.Key))
                {
                    return b.Value;
                }

                var u = (frame - a.Key) / (b.Key - a.Key);
                return Mathf.Lerp(a.Value, b.Value, u);
            }

            return keys[keys.Count - 1].Value;
        }

        private static float PeakAbs(List<KeyValuePair<float, float>> keys)
        {
            var peak = 0f;
            if (keys == null)
            {
                return peak;
            }

            for (var i = 0; i < keys.Count; i++)
            {
                var v = Mathf.Abs(keys[i].Value);
                if (v > peak)
                {
                    peak = v;
                }
            }

            return peak;
        }

        private static void ApplyHeuristicFallback(
            string stateName,
            float[] blinkL,
            float[] blinkR,
            float[] squintL,
            float[] squintR,
            float[] smile,
            float[] frown,
            float[] jaw,
            float[] eyeWideL,
            float[] eyeWideR,
            float[] browInner,
            float[] browDown)
        {
            var heuristic = DashboardRigFaceOverlay.Resolve(stateName);
            if (!DashboardRigFaceOverlay.IsOneShotExpression(stateName) &&
                stateName.IndexOf("sad", StringComparison.OrdinalIgnoreCase) < 0 &&
                stateName.IndexOf("gasp", StringComparison.OrdinalIgnoreCase) < 0 &&
                stateName.IndexOf("shock", StringComparison.OrdinalIgnoreCase) < 0 &&
                stateName.IndexOf("yawn", StringComparison.OrdinalIgnoreCase) < 0 &&
                stateName.IndexOf("sigh", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            // Capture once before writing — MaxAbs inside the loop would stop after the first key.
            var fillSmile = MaxAbs(smile) < 1f && heuristic.Smile > 1f;
            var fillFrown = MaxAbs(frown) < 1f && heuristic.Frown > 1f;
            var fillJaw = MaxAbs(jaw) < 1f && heuristic.Jaw > 1f;
            var fillBlinkL = MaxAbs(blinkL) < 1f && heuristic.BlinkLeft > 1f;
            var fillBlinkR = MaxAbs(blinkR) < 1f && heuristic.BlinkRight > 1f;
            var fillWideL = MaxAbs(eyeWideL) < 1f && heuristic.EyeWideLeft > 1f;
            var fillWideR = MaxAbs(eyeWideR) < 1f && heuristic.EyeWideRight > 1f;
            var fillBrowInner = MaxAbs(browInner) < 1f && heuristic.BrowInner > 1f;
            var fillBrowDown = MaxAbs(browDown) < 1f && heuristic.BrowDown > 1f;
            var fillSquintL = MaxAbs(squintL) < 1f && heuristic.SquintLeft > 1f;
            var fillSquintR = MaxAbs(squintR) < 1f && heuristic.SquintRight > 1f;

            for (var i = 0; i < smile.Length; i++)
            {
                var t = smile.Length <= 1 ? 0f : (float)i / (smile.Length - 1);
                var envelope = DashboardRigFaceOverlay.PlateauEnvelope(t);
                if (fillSmile)
                {
                    smile[i] = Mathf.Max(smile[i], heuristic.Smile * envelope);
                }

                if (fillFrown)
                {
                    frown[i] = Mathf.Max(frown[i], heuristic.Frown * envelope);
                }

                if (fillJaw)
                {
                    jaw[i] = Mathf.Max(jaw[i], heuristic.Jaw * envelope);
                }

                if (fillBlinkL)
                {
                    blinkL[i] = Mathf.Max(blinkL[i], heuristic.BlinkLeft * envelope);
                }

                if (fillBlinkR)
                {
                    blinkR[i] = Mathf.Max(blinkR[i], heuristic.BlinkRight * envelope);
                }

                if (fillWideL)
                {
                    eyeWideL[i] = Mathf.Max(eyeWideL[i], heuristic.EyeWideLeft * envelope);
                }

                if (fillWideR)
                {
                    eyeWideR[i] = Mathf.Max(eyeWideR[i], heuristic.EyeWideRight * envelope);
                }

                if (fillBrowInner)
                {
                    browInner[i] = Mathf.Max(browInner[i], heuristic.BrowInner * envelope);
                }

                if (fillBrowDown)
                {
                    browDown[i] = Mathf.Max(browDown[i], heuristic.BrowDown * envelope);
                }

                if (fillSquintL)
                {
                    squintL[i] = Mathf.Max(squintL[i], heuristic.SquintLeft * envelope);
                }

                if (fillSquintR)
                {
                    squintR[i] = Mathf.Max(squintR[i], heuristic.SquintRight * envelope);
                }
            }
        }

        private static float MaxAbs(float[] samples)
        {
            var peak = 0f;
            if (samples == null)
            {
                return peak;
            }

            for (var i = 0; i < samples.Length; i++)
            {
                var v = Mathf.Abs(samples[i]);
                if (v > peak)
                {
                    peak = v;
                }
            }

            return peak;
        }

        private static List<KeyValuePair<float, float>> Prefer(
            List<KeyValuePair<float, float>> primary,
            List<KeyValuePair<float, float>> fallback)
        {
            return HasMotion(primary) ? primary : fallback;
        }

        private static bool HasMotion(List<KeyValuePair<float, float>> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return false;
            }

            var first = keys[0].Value;
            for (var i = 1; i < keys.Count; i++)
            {
                if (Mathf.Abs(keys[i].Value - first) > 0.001f)
                {
                    return true;
                }
            }

            return Mathf.Abs(first) > 0.001f;
        }

        private static List<KeyValuePair<float, float>> ExtractScalarKeys(string json, string channelName)
        {
            var result = new List<KeyValuePair<float, float>>();
            var marker = "\"" + channelName + "\"";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return result;
            }

            var slice = json.Substring(start, Math.Min(2500, json.Length - start));
            var anim = Regex.Match(
                slice,
                "\"animcurve\"\\s*:\\s*\\{.*?\"keyTime\"\\s*:\\s*\\[(?<times>[^\\]]*)\\].*?\"keyValue\"\\s*:\\s*\\[(?<values>[^\\]]*)\\]",
                RegexOptions.Singleline);
            if (!anim.Success)
            {
                return result;
            }

            AppendPairs(anim.Groups["times"].Value, anim.Groups["values"].Value, result);
            return result;
        }

        private static List<KeyValuePair<float, float>> ExtractComponentKeys(
            string json,
            string channelName,
            int componentIndex)
        {
            var result = new List<KeyValuePair<float, float>>();
            var marker = "\"" + channelName + "\"";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return result;
            }

            var slice = json.Substring(start, Math.Min(6000, json.Length - start));
            var componentsMatch = Regex.Match(
                slice,
                "\"components\"\\s*:\\s*\\[(?<body>.*?)\\]\\s*\\}",
                RegexOptions.Singleline);
            if (!componentsMatch.Success)
            {
                return result;
            }

            var comps = SplitTopObjects(componentsMatch.Groups["body"].Value);
            if (componentIndex < 0 || componentIndex >= comps.Count)
            {
                return result;
            }

            var body = comps[componentIndex];
            var constant = Regex.Match(body, "\"constant\"\\s*:\\s*(?<v>-?[0-9.eE+]+)");
            if (constant.Success)
            {
                result.Add(new KeyValuePair<float, float>(
                    1f,
                    float.Parse(constant.Groups["v"].Value, CultureInfo.InvariantCulture)));
                return result;
            }

            var anim = Regex.Match(
                body,
                "\"animcurve\"\\s*:\\s*\\{.*?\"keyTime\"\\s*:\\s*\\[(?<times>[^\\]]*)\\].*?\"keyValue\"\\s*:\\s*\\[(?<values>[^\\]]*)\\]",
                RegexOptions.Singleline);
            if (anim.Success)
            {
                AppendPairs(anim.Groups["times"].Value, anim.Groups["values"].Value, result);
            }

            return result;
        }

        private static List<string> SplitTopObjects(string body)
        {
            var list = new List<string>();
            var depth = 0;
            var start = -1;
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }

                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        list.Add(body.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            return list;
        }

        private static void AppendPairs(string timesRaw, string valuesRaw, List<KeyValuePair<float, float>> result)
        {
            var times = timesRaw.Split(',');
            var values = valuesRaw.Split(',');
            var count = Math.Min(times.Length, values.Length);
            for (var i = 0; i < count; i++)
            {
                if (!float.TryParse(times[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                {
                    continue;
                }

                if (!float.TryParse(values[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    continue;
                }

                result.Add(new KeyValuePair<float, float>(t, v));
            }
        }

        private static int ReadInt(string json, string key, int fallback)
        {
            var m = Regex.Match(json, key + "\\s*:\\s*(-?[0-9]+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : fallback;
        }

        private static float ReadFloat(string json, string key, float fallback)
        {
            var m = Regex.Match(json, key + "\\s*:\\s*(-?[0-9.eE+]+)");
            return m.Success &&
                   float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : fallback;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Replace(' ', '_');
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            var parts = assetFolder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
