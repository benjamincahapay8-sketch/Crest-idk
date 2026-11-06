// Eclipse Crest v3 - All Vision Android port. NO Harmony (this port's 0Harmony can't be loaded by plugins).
// Needs only: BepInEx.dll + UnityEngine.CoreModule + UnityEngine.ImageConversionModule to compile.
// Everything game-related is reached by reflection and logged under "[Eclipse]".
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace EclipseCrestMod
{
    [BepInPlugin("com.eclipse.crest", "Eclipse Crest", "3.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin I;
        void Awake()
        {
            I = this;
            Logger.LogInfo("[Eclipse] step 1: Awake started");
            try { Boot.StageConfig(this); Logger.LogInfo("[Eclipse] step 2: config ok"); }
            catch (Exception e) { Logger.LogError("[Eclipse] step 2 FAILED: " + e.Message); }
            try { Boot.StageComponent(); Logger.LogInfo("[Eclipse] step 3: component added"); }
            catch (Exception e) { Logger.LogError("[Eclipse] step 3 FAILED: " + e.Message); }
            Logger.LogInfo("[Eclipse] loaded v3");
        }
        public static void Log(string s) { if (I != null) I.Logger.LogInfo("[Eclipse] " + s); }
        public static void Warn(string s) { if (I != null) I.Logger.LogWarning("[Eclipse] " + s); }
    }

    static class Boot
    {
        static ConfigEntry<bool> enabled;
        public static bool On { get { return enabled == null || enabled.Value; } }
        public static void StageConfig(BaseUnityPlugin p) { enabled = p.Config.Bind("General", "Enabled", true, "Turn the Eclipse moveset on/off"); }
        public static void StageComponent()
        {
            var go = new GameObject("EclipseCrest");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Eclipse>();
        }
    }

    // ---------- plain reflection helpers (no Harmony) ----------
    static class R
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        public static Type T(string name)
        {
            Type t;
            if (typeCache.TryGetValue(name, out t)) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(name, false); } catch { t = null; }
                if (t != null) break;
            }
            typeCache[name] = t; return t;
        }

        public static object Member(object o, Type t, string name)
        {
            for (var tt = t; tt != null; tt = tt.BaseType)
            {
                var f = tt.GetField(name, All);
                if (f != null) { try { return f.GetValue(f.IsStatic ? null : o); } catch { } }
                var p = tt.GetProperty(name, All);
                if (p != null && p.GetIndexParameters().Length == 0) { try { return p.GetValue(o, null); } catch { } }
            }
            return null;
        }
        public static object Member(object o, string name) { return o == null ? null : Member(o, o.GetType(), name); }

        public static List<string> MemberNames(object o)
        {
            var list = new List<string>();
            for (var tt = o.GetType(); tt != null && tt != typeof(object); tt = tt.BaseType)
            {
                foreach (var f in tt.GetFields(All)) list.Add(f.Name);
                foreach (var p in tt.GetProperties(All)) list.Add(p.Name);
            }
            return list;
        }

        static Component heroCache;
        public static object Hero()
        {
            if (heroCache != null) return heroCache;
            var t = T("HeroController"); if (t == null) return null;
            var inst = Member(null, t, "instance") ?? Member(null, t, "_instance");
            var c = inst as Component;
            if (c == null) { try { c = UnityEngine.Object.FindObjectOfType(t) as Component; } catch { } }
            heroCache = c; return c;
        }
        public static object PlayerData(object hero)
        {
            var pd = Member(hero, "playerData");
            if (pd != null) return pd;
            var t = T("PlayerData"); return t == null ? null : Member(null, t, "instance");
        }
        public static T Get<T>(object o, params string[] path)
        {
            try
            {
                object cur = o;
                foreach (var p in path) { cur = Member(cur, p); if (cur == null) return default(T); }
                return (T)Convert.ChangeType(cur, typeof(T));
            }
            catch { return default(T); }
        }
        public static bool Call(object o, string m, params object[] a)
        {
            if (o == null) return false;
            for (var tt = o.GetType(); tt != null; tt = tt.BaseType)
            {
                foreach (var mi in tt.GetMethods(All))
                {
                    if (mi.Name != m || mi.IsStatic) continue;
                    var ps = mi.GetParameters(); if (ps.Length < a.Length) continue;
                    var args = new object[ps.Length]; bool ok = true;
                    for (int i = 0; i < ps.Length && ok; i++)
                    {
                        try
                        {
                            if (i < a.Length) args[i] = Convert.ChangeType(a[i], ps[i].ParameterType);
                            else if (ps[i].IsOptional) args[i] = ps[i].DefaultValue;
                            else ok = false;
                        }
                        catch { ok = false; }
                    }
                    if (!ok) continue;
                    try { mi.Invoke(o, args); return true; } catch { }
                }
            }
            return false;
        }
        static HashSet<string> warned = new HashSet<string>();
        public static void CallWarn(object o, string m, params object[] a)
        {
            if (!Call(o, m, a) && warned.Add(m)) Plugin.Warn("method not found/failed: " + m);
        }
    }

    // ---------- effect animations ----------
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
                var ic = R.T("UnityEngine.ImageConversion");
                ic.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) }).Invoke(null, new object[] { tex, File.ReadAllBytes(path) });
                int w = tex.width / Frames; s = new Sprite[Frames];
                for (int i = 0; i < Frames; i++) s[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Warn("sprite '" + name + "' failed: " + e.Message); s = null; }
            cache[name] = s; return s;
        }
        public static void Play(string name, Vector3 pos, float size, bool follow, float fps)
        {
            var fr = Load(name); if (fr == null) return;
            var go = new GameObject("Eclipse_" + name); go.transform.position = pos;
            float units = size / (fr[0].rect.width / 100f); go.transform.localScale = new Vector3(units, units, 1);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sortingOrder = 500;
            go.AddComponent<FxPlayer>().Init(sr, fr, fps, follow ? R.Hero() as Component : null);
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
        const int MaskCount = 2, MaskHp = 2;
        const float VoidMult = 0.75f, MoonlitMult = 1.15f, VoidTime = 8f;
        const int EchoEvery = 4, WhirlSilk = 1, BurstSilk = 2;
        const float ChordWindow = 0.10f;
        int[] masks = { MaskHp, MaskHp };
        float voidUntil; int hits; float lastHp = -1;
        bool VoidOn { get { return Time.time < voidUntil; } }
        Skill lastWhite = Skill.None;
        bool diving, wasGrounded, dumped;
        float tAtk = -9, tDash = -9, tSkill = -9;

        // ----- input via the game's own action objects -----
        object Acts()
        {
            var ih = R.T("InputHandler"); if (ih == null) return null;
            object inst = R.Member(null, ih, "Instance") ?? R.Member(null, ih, "instance");
            if (inst == null) { try { inst = UnityEngine.Object.FindObjectOfType(ih); } catch { } }
            var acts = R.Member(inst, "inputActions");
            if (acts != null && !dumped)
            {
                dumped = true;
                var names = R.MemberNames(acts);
                Plugin.Log("input actions found. members: " + string.Join(", ", names.ToArray()));
            }
            return acts;
        }
        bool Pressed(object acts, string name) { var a = R.Member(acts, name); return a != null && R.Get<bool>(a, "WasPressed"); }
        bool Held(object acts, string name) { var a = R.Member(acts, name); return a != null && R.Get<bool>(a, "IsPressed"); }

        void Update()
        {
            try
            {
                if (!Boot.On) return;
                var hero = R.Hero(); if (hero == null) return;
                TickMasks(hero);
                var acts = Acts(); if (acts == null) return;

                float now = Time.time;
                if (Pressed(acts, "Attack")) tAtk = now;
                if (Pressed(acts, "Dash")) tDash = now;
                if (Pressed(acts, "Cast")) tSkill = now;
                bool down = Held(acts, "Down"), up = Held(acts, "Up"), castHeld = Held(acts, "Cast");
                bool skillNow = now - tSkill < 0.03f, atkNow = now - tAtk < 0.03f, dashNow = now - tDash < 0.03f;
                bool nearSA = Mathf.Abs(tSkill - tAtk) <= ChordWindow, nearDA = Mathf.Abs(tDash - tAtk) <= ChordWindow, nearSD = Mathf.Abs(tSkill - tDash) <= ChordWindow;

                if (skillNow && down) White(Skill.MeteorDrop, hero);
                else if ((skillNow || atkNow) && nearSA) White(Skill.MoonWhirl, hero);
                else if ((dashNow || atkNow) && nearDA) White(Skill.ThreadLunge, hero);
                else if ((skillNow || dashNow) && nearSD) Black(Skill.AbyssStep, hero);
                else if (castHeld && up && atkNow) Black(Skill.VoidTendrils, hero);
                else if (castHeld && down && atkNow) Black(Skill.UmbralBurst, hero);

                bool g = Grounded(hero);
                if (diving && g && !wasGrounded) Land(hero);
                wasGrounded = g;
            }
            catch (Exception e) { Plugin.Warn("update: " + e.Message); }
        }

        // ----- Eclipse Masks: soak damage by healing it back (2 masks x 2 HP) -----
        void TickMasks(object hero)
        {
            var pd = R.PlayerData(hero); if (pd == null) return;
            int hp = R.Get<int>(pd, "health");
            if (lastHp >= 0 && hp < (int)lastHp)
            {
                int drop = (int)lastHp - hp, soaked = 0;
                for (int m = 0; m < masks.Length && drop > 0; m++) { int t = Mathf.Min(drop, masks[m]); masks[m] -= t; drop -= t; soaked += t; }
                if (soaked > 0) { R.CallWarn(hero, "AddHealth", soaked); hp += soaked; Plugin.Log("masks soaked " + soaked + " (left " + masks[0] + "," + masks[1] + ")"); }
            }
            lastHp = hp;
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
                    Vel(hero, Facing(hero) * 28f, 0); Invuln(hero, 0.4f);
                    Fx.Play("ThreadLunge", P(hero) + new Vector3(Facing(hero) * 2f, 0, 0), 6f, false, 36f); Hit(hero, 2.4f, 1.1f); break;
            }
            Plugin.Log("white skill: " + s);
            lastWhite = s;
        }
        void Land(object hero)
        {
            diving = false;
            Fx.Play("MeteorShock", P(hero) + new Vector3(0, -0.6f, 0), 9f, false, 24f);
            if (Hit(hero, 4f, 1.2f) > 0) R.CallWarn(hero, "AddHealth", 1);
        }

        // ----- black skills: void, weaken damage for a while -----
        void Black(Skill s, object hero)
        {
            switch (s)
            {
                case Skill.VoidTendrils: Fx.Play("VoidTendrils", P(hero), 9f, true, 14f); Hit(hero, 4.5f, 0.8f); break;
                case Skill.AbyssStep:
                    var tr = ((Component)hero).transform; tr.position += new Vector3(Facing(hero) * 6f, 0, 0); Invuln(hero, 0.3f); break;
                case Skill.UmbralBurst:
                    if (!TakeSilk(hero, BurstSilk)) return;
                    Fx.Play("UmbralBurst", P(hero), 11f, false, 24f); Hit(hero, 5.5f, 2.0f); break;
            }
            Plugin.Log("black skill: " + s);
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
        void Invuln(object h, float t) { R.CallWarn(h, "StartInvulnerable", t); }
        bool TakeSilk(object h, int n)
        {
            int silk = R.Get<int>(R.PlayerData(h), "silk");
            if (silk < n) return false; R.CallWarn(h, "TakeSilk", n); return true;
        }
        void AddSilk(object h, int n) { R.CallWarn(h, "AddSilk", n, false); }
        int Hit(object hero, float radius, float mult)
        {
            var pd = R.PlayerData(hero);
            bool full = R.Get<int>(pd, "health") >= R.Get<int>(pd, "maxHealth") && R.Get<int>(pd, "maxHealth") > 0;
            float m = mult * (full ? MoonlitMult : 1f) * (VoidOn ? VoidMult : 1f);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(5 * m)), n = 0;
            var p2d = R.T("UnityEngine.Physics2D"); var hmT = R.T("HealthManager");
            if (p2d == null || hmT == null) return 0;
            var mi = p2d.GetMethod("OverlapCircleAll", new[] { typeof(Vector2), typeof(float) });
            if (mi == null) return 0;
            Vector3 pos = P(hero);
            var res = mi.Invoke(null, new object[] { new Vector2(pos.x, pos.y), radius }) as Array;
            if (res == null) return 0;
            foreach (var o in res)
            {
                var c = o as Component; if (c == null) continue;
                var hm = c.GetComponent(hmT); if (hm == null) continue;
                if (R.Call(hm, "ApplyExtraDamage", dmg)) { n++; if (++hits % EchoEvery == 0) AddSilk(hero, 1); }
            }
            return n;
        }
    }
}
