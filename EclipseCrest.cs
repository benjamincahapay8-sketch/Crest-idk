// Eclipse Crest v2 - for the All Vision Android port (BepInEx + HybridCLR, managed plugin)
// UNTESTED. Written to need ONLY these references: BepInEx.dll, 0Harmony.dll,
// UnityEngine.CoreModule, UnityEngine.Physics2DModule, UnityEngine.InputLegacyModule,
// UnityEngine.ImageConversionModule (for the effect sprites).
// Game classes are reached by name at runtime (reflection), so a wrong guess
// logs a warning instead of crashing. Check BepInEx/LogOutput.log for "[Eclipse]".
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace EclipseCrestMod
{
    [BepInPlugin("com.eclipse.crest", "Eclipse Crest", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin I;
        void Awake()
        {
            I = this;
            Logger.LogInfo("[Eclipse] step 1: Awake started");
            try { Boot.StageConfig(this); Logger.LogInfo("[Eclipse] step 2: config ok"); }
            catch (Exception e) { Logger.LogError("[Eclipse] step 2 FAILED: " + e); }
            try { Boot.StagePatch(); Logger.LogInfo("[Eclipse] step 3: patches ok"); }
            catch (Exception e) { Logger.LogError("[Eclipse] step 3 FAILED: " + e); }
            try { Boot.StageComponent(); Logger.LogInfo("[Eclipse] step 4: component added"); }
            catch (Exception e) { Logger.LogError("[Eclipse] step 4 FAILED: " + e); }
            Logger.LogInfo("[Eclipse] loaded");
        }
        public static void Log(string s) { I.Logger.LogInfo("[Eclipse] " + s); }
        public static void Warn(string s) { I.Logger.LogWarning("[Eclipse] " + s); }
    }

    // Everything risky lives here, so a failure is caught and logged instead of silently killing the plugin.
    static class Boot
    {
        static ConfigEntry<bool> enabled;
        public static bool On { get { return enabled == null || enabled.Value; } }
        public static void StageConfig(BaseUnityPlugin p) { enabled = p.Config.Bind("General", "Enabled", true, "Turn the Eclipse moveset on/off"); }
        public static void StagePatch() { new Harmony("com.eclipse.crest").PatchAll(typeof(Patches)); }
        public static void StageComponent()
        {
            var go = new GameObject("EclipseCrest"); UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Eclipse>();
        }
    }

    // ---------- reflection helpers ----------
    static class R
    {
        public static Type T(string n) { return AccessTools.TypeByName(n); }
        public static object Hero()
        {
            var t = T("HeroController"); if (t == null) return null;
            return AccessTools.Property(t, "instance")?.GetValue(null) ?? AccessTools.Field(t, "_instance")?.GetValue(null);
        }
        public static bool Call(object o, string m, params object[] a)
        {
            if (o == null) return false;
            foreach (var mi in o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (mi.Name != m) continue;
                var ps = mi.GetParameters(); if (ps.Length < a.Length) continue;
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    args[i] = i < a.Length ? Convert.ChangeType(a[i], ps[i].ParameterType) : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                try { mi.Invoke(o, args); return true; } catch { }
            }
            Plugin.Warn("method not found/failed: " + o.GetType().Name + "." + m); return false;
        }
        public static T Get<T>(object o, params string[] path)
        {
            try { var tr = Traverse.Create(o); foreach (var p in path) { var f = tr.Field(p); tr = f.GetValue() != null ? f : tr.Property(p); }
                return (T)Convert.ChangeType(tr.GetValue(), typeof(T)); } catch { return default(T); }
        }
    }


    // ---------- custom effect animations (8-frame PNG strips in plugins/EclipseCrest/Sprites) ----------
    public static class Fx
    {
        static Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
        const int Frames = 8;
        static Sprite[] Load(string name)
        {
            Sprite[] s; if (cache.TryGetValue(name, out s)) return s;
            try
            {
                string path = Path.Combine(Paths.PluginPath, "EclipseCrest/Sprites/" + name + ".png");
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var li = typeof(Texture2D).Assembly.GetType("UnityEngine.ImageConversion") ?? AccessTools.TypeByName("UnityEngine.ImageConversion");
                li.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) }).Invoke(null, new object[] { tex, File.ReadAllBytes(path) });
                tex.filterMode = FilterMode.Bilinear;
                int w = tex.width / Frames; s = new Sprite[Frames];
                for (int i = 0; i < Frames; i++) s[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Warn("sprite '" + name + "' failed: " + e.Message); s = null; }
            cache[name] = s; return s;
        }
        public static void Play(string name, Vector3 pos, float size, bool followHero = false, float fps = 24f)
        {
            var fr = Load(name); if (fr == null) return;
            var go = new GameObject("Eclipse_" + name); go.transform.position = pos;
            float units = size / (fr[0].rect.width / 100f); go.transform.localScale = new Vector3(units, units, 1);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sortingOrder = 500;
            go.AddComponent<FxPlayer>().Init(sr, fr, fps, followHero ? R.Hero() as Component : null);
        }
    }
    public class FxPlayer : MonoBehaviour
    {
        SpriteRenderer sr; Sprite[] fr; float fps; float t; Component follow;
        public void Init(SpriteRenderer s, Sprite[] f, float rate, Component fol) { sr = s; fr = f; fps = rate; follow = fol; sr.sprite = f[0]; }
        void Update()
        {
            t += Time.deltaTime; int i = (int)(t * fps);
            if (i >= fr.Length) { Destroy(gameObject); return; }
            sr.sprite = fr[i]; if (follow != null) transform.position = follow.transform.position;
        }
    }

    public enum Skill { None, MeteorDrop, MoonWhirl, ThreadLunge, VoidTendrils, AbyssStep, UmbralBurst }

    public class Eclipse : MonoBehaviour
    {
        // ----- tuning (all yours to change) -----
        const int MaskCount = 2, MaskHp = 2;
        const float VoidMult = 0.75f, MoonlitMult = 1.15f, VoidTime = 8f;
        const int EchoEvery = 4, WhirlSilk = 1, BurstSilk = 2;
        const float ChordWindow = 0.10f;
        public static int[] Masks = { MaskHp, MaskHp };
        static float voidUntil;
        public static bool VoidOn { get { return Time.time < voidUntil; } }
        public static int Hits;
        Skill lastWhite = Skill.None;
        bool diving, wasGrounded;
        float tAtk = -9, tDash = -9, tSkill = -9;

        // ----- input: reads the game's own actions (works with the port's touch buttons) -----
        bool Pressed(string action)
        {
            try
            {
                var ih = R.T("InputHandler"); if (ih == null) return false;
                var inst = AccessTools.Property(ih, "Instance")?.GetValue(null) ?? AccessTools.Field(ih, "instance")?.GetValue(null);
                var acts = AccessTools.Field(ih, "inputActions")?.GetValue(inst) ?? AccessTools.Property(ih, "inputActions")?.GetValue(inst);
                var a = AccessTools.Property(acts.GetType(), action)?.GetValue(acts) ?? AccessTools.Field(acts.GetType(), action)?.GetValue(acts);
                return (bool)AccessTools.Property(a.GetType(), "WasPressed").GetValue(a);
            }
            catch { return false; }
        }
        bool Held(string action)
        {
            try
            {
                var ih = R.T("InputHandler");
                var inst = AccessTools.Property(ih, "Instance")?.GetValue(null) ?? AccessTools.Field(ih, "instance")?.GetValue(null);
                var acts = AccessTools.Field(ih, "inputActions")?.GetValue(inst) ?? AccessTools.Property(ih, "inputActions")?.GetValue(inst);
                var a = AccessTools.Property(acts.GetType(), action)?.GetValue(acts) ?? AccessTools.Field(acts.GetType(), action)?.GetValue(acts);
                return (bool)AccessTools.Property(a.GetType(), "IsPressed").GetValue(a);
            }
            catch { return false; }
        }

        void Update()
        {
            if (!Boot.On) return;
            var hero = R.Hero(); if (hero == null) return;
            try
            {
                if (Pressed("Attack")) tAtk = Time.time;
                if (Pressed("Dash")) tDash = Time.time;
                if (Pressed("Cast")) tSkill = Time.time;      // "Cast" = the skill button
                bool down = Held("Down"), up = Held("Up");
                bool near(float a, float b) { return Mathf.Abs(a - b) <= ChordWindow; }
                float now = Time.time;
                bool skillNow = now - tSkill < 0.02f, atkNow = now - tAtk < 0.02f, dashNow = now - tDash < 0.02f;

                if (skillNow && down) White(Skill.MeteorDrop, hero);
                else if ((skillNow || atkNow) && near(tSkill, tAtk)) White(Skill.MoonWhirl, hero);
                else if ((dashNow || atkNow) && near(tDash, tAtk)) White(Skill.ThreadLunge, hero);
                else if ((skillNow || dashNow) && near(tSkill, tDash)) Black(Skill.AbyssStep, hero);
                else if (Held("Cast") && up && skillNow) Black(Skill.VoidTendrils, hero);
                else if (Held("Cast") && down && atkNow) Black(Skill.UmbralBurst, hero);

                bool g = Grounded(hero);
                if (diving && g && !wasGrounded) Land(hero);
                wasGrounded = g;
            }
            catch (Exception e) { Plugin.Warn("update: " + e.Message); }
        }

        // ----- white skills: no cooldown, never the same one twice in a row -----
        void White(Skill s, object hero)
        {
            if (s == lastWhite) return;
            switch (s)
            {
                case Skill.MeteorDrop:
                    if (Grounded(hero)) return;
                    diving = true; Vel(hero, 0, -45f); Invuln(hero, 0.2f); break;
                case Skill.MoonWhirl:
                    if (!TakeSilk(hero, WhirlSilk)) return;
                    Fx.Play("MoonWhirl", P(hero), 7f, true, 30f); Hit(hero, 3.2f, 1.0f); break;
                case Skill.ThreadLunge:
                    Vel(hero, Facing(hero) * 28f, 0); Invuln(hero, 0.4f); Fx.Play("ThreadLunge", P(hero) + new Vector3(Facing(hero) * 2f, 0, 0), 6f, false, 36f); Hit(hero, 2.4f, 1.1f); break;
            }
            lastWhite = s;
        }
        void Land(object hero)
        {
            diving = false;
            Fx.Play("MeteorShock", P(hero) + new Vector3(0, -0.6f, 0), 9f);
            if (Hit(hero, 4f, 1.2f) > 0) R.Call(hero, "AddHealth", 1);   // heal ONLY if the slam hits something
        }

        // ----- black skills: void, weakens nail for a while -----
        void Black(Skill s, object hero)
        {
            switch (s)
            {
                case Skill.VoidTendrils: Fx.Play("VoidTendrils", P(hero), 9f, true, 14f); Hit(hero, 4.5f, 0.8f); break;
                case Skill.AbyssStep: var t = ((Component)hero).transform; t.position += new Vector3(Facing(hero) * 6f, 0, 0); Invuln(hero, 0.3f); break;
                case Skill.UmbralBurst: if (!TakeSilk(hero, BurstSilk)) return; Fx.Play("UmbralBurst", P(hero), 11f, false, 24f); Hit(hero, 5.5f, 2.0f); break;
            }
            voidUntil = Time.time + VoidTime; lastWhite = Skill.None;
        }

        // ----- hero helpers -----
        Vector3 P(object h) { return ((Component)h).transform.position; }
        float Facing(object h) { return ((Component)h).transform.localScale.x >= 0 ? 1f : -1f; }
        bool Grounded(object h) { return R.Get<bool>(h, "cState", "onGround"); }
        void Vel(object h, float x, float y)
        {
            var rbT = R.T("UnityEngine.Rigidbody2D"); if (rbT == null) return;
            var rb = ((Component)h).GetComponent(rbT); if (rb == null) return;
            var lv = rbT.GetProperty("linearVelocity") ?? rbT.GetProperty("velocity");
            if (lv != null) lv.SetValue(rb, new Vector2(x, y), null);
        }
        void Invuln(object h, float t) { R.Call(h, "StartInvulnerable", t); }
        bool TakeSilk(object h, int n)
        {
            int silk = R.Get<int>(h, "playerData", "silk");
            if (silk < n) return false; R.Call(h, "TakeSilk", n); return true;
        }
        public static void AddSilk(int n) { var h = R.Hero(); R.Call(h, "AddSilk", n, false); }
        int Hit(object hero, float radius, float mult)
        {
            int n = 0; int dmg = Mathf.Max(1, Mathf.RoundToInt(5 * mult * (VoidOn ? VoidMult : 1f)));
            var p2d = R.T("UnityEngine.Physics2D"); var hmT = R.T("HealthManager");
            if (p2d == null || hmT == null) return 0;
            var mi = p2d.GetMethod("OverlapCircleAll", new[] { typeof(Vector2), typeof(float) });
            if (mi == null) return 0;
            Vector3 pos = ((Component)hero).transform.position;
            var res = mi.Invoke(null, new object[] { new Vector2(pos.x, pos.y), radius }) as Array;
            if (res == null) return 0;
            foreach (var o in res)
            {
                var c = o as Component; if (c == null) continue;
                var hm = c.GetComponent(hmT); if (hm == null) continue;
                if (R.Call(hm, "ApplyExtraDamage", dmg)) n++;
            }
            return n;
        }
    }

    // ---------- Harmony patches (by name, so missing methods just log) ----------
    static class Patches
    {
        // nail damage: Moonlit Edge (+15% at full HP), void penalty (-25%), Silk Echo
        [HarmonyPatch]
        static class NailDmg
        {
            static MethodBase TargetMethod() { return AccessTools.Method(R.T("HealthManager"), "TakeDamage"); }
            static void Prefix(object[] __args)
            {
                if (!Boot.On || __args == null || __args.Length == 0) return;
                try
                {
                    var hi = __args[0]; var tr = Traverse.Create(hi);
                    if (tr.Field("AttackType").GetValue().ToString() != "Nail") return;
                    float d = tr.Field("DamageDealt").GetValue<int>();
                    var h = R.Hero();
                    if (R.Get<int>(h, "playerData", "health") >= R.Get<int>(h, "playerData", "maxHealth")) d *= 1.15f;
                    if (Eclipse.VoidOn) d *= 0.75f;
                    tr.Field("DamageDealt").SetValue(Mathf.Max(1, Mathf.RoundToInt(d)));
                    __args[0] = hi;                               // write the boxed struct back
                    if (++Eclipse.Hits % 4 == 0) Eclipse.AddSilk(1);
                }
                catch (Exception e) { Plugin.Warn("nail patch: " + e.Message); }
            }
        }

        // Eclipse Masks soak damage first (2 masks x 2 HP)
        [HarmonyPatch]
        static class HeroHurt
        {
            static MethodBase TargetMethod() { return AccessTools.Method(R.T("HeroController"), "TakeDamage"); }
            static void Prefix(MethodBase __originalMethod, object[] __args)
            {
                if (!Boot.On) return;
                try
                {
                    var ps = __originalMethod.GetParameters();
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (ps[i].Name != "damageAmount") continue;
                        int dmg = (int)__args[i];
                        for (int m = 0; m < Eclipse.Masks.Length && dmg > 0; m++)
                        { int t = Mathf.Min(dmg, Eclipse.Masks[m]); Eclipse.Masks[m] -= t; dmg -= t; }
                        __args[i] = dmg;
                    }
                }
                catch (Exception e) { Plugin.Warn("hurt patch: " + e.Message); }
            }
        }
    }
}
