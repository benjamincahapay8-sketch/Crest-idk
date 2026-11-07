// Eclipse Crest v4 - All Vision Android port. Harmony is found by reflection (on this port it lives inside BepInEx.dll).
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
    [BepInPlugin("com.eclipse.crest", "Eclipse Crest", "4.0.0")]
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
            try { Boot.StagePatch(); Logger.LogInfo("[Eclipse] step 4: nail-hit patch ok"); }
            catch (Exception e) { Logger.LogError("[Eclipse] step 4 FAILED (passives off, skills still work): " + e.Message); }
            Logger.LogInfo("[Eclipse] loaded v4");
        }
        public static void Log(string s) { if (I != null) I.Logger.LogInfo("[Eclipse] " + s); }
        public static void Warn(string s) { if (I != null) I.Logger.LogWarning("[Eclipse] " + s); }
    }

    static class Boot
    {
        static ConfigEntry<bool> enabled;
        public static bool On { get { return enabled == null || enabled.Value; } }
        public static void StageConfig(BaseUnityPlugin p) { enabled = p.Config.Bind("General", "Enabled", true, "Turn the Eclipse moveset on/off"); }
        public static void StagePatch()
        {
            Type hT = R.T("HarmonyLib.Harmony"), hmT = R.T("HarmonyLib.HarmonyMethod");
            if (hT == null || hmT == null) throw new Exception("HarmonyLib not found in any loaded assembly");
            object harmony = Activator.CreateInstance(hT, new object[] { "com.eclipse.crest" });
            var target = R.T("HealthManager"); if (target == null) throw new Exception("HealthManager type not found");
            MethodBase orig = null;
            foreach (var m in target.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (m.Name == "TakeDamage") { orig = m; break; }
            if (orig == null) throw new Exception("HealthManager.TakeDamage not found");
            var pre = typeof(PatchFns).GetMethod("NailPrefix");
            object hm = Activator.CreateInstance(hmT, new object[] { pre });
            foreach (var pm in hT.GetMethods())
            {
                if (pm.Name != "Patch") continue;
                var ps = pm.GetParameters();
                if (ps.Length < 2 || ps[0].ParameterType != typeof(MethodBase)) continue;
                var args = new object[ps.Length]; args[0] = orig; args[1] = hm;
                pm.Invoke(harmony, args); return;
            }
            throw new Exception("Harmony.Patch method not found");
        }
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

        public static bool SetMember(object o, string name, object v)
        {
            for (var tt = o.GetType(); tt != null; tt = tt.BaseType)
            {
                var f = tt.GetField(name, All);
                if (f != null) { f.SetValue(o, Convert.ChangeType(v, f.FieldType)); return true; }
                var p = tt.GetProperty(name, All);
                if (p != null && p.CanWrite) { p.SetValue(o, Convert.ChangeType(v, p.PropertyType), null); return true; }
            }
            return false;
        }
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

    public enum Skill { None, Moonfall, LunarRise, CrescentCut, VoidRend, UmbralVeil, EventHorizon }

    public class Eclipse : MonoBehaviour
    {
        public static Eclipse Inst;
        const int MaskCount = 2, MaskHp = 2;
        const float VoidMult = 0.75f, MoonlitMult = 1.15f, VoidTime = 8f;
        const int EchoEvery = 4, CutSilk = 1, RendSilk = 1, HorizonSilk = 2;
        const float ChordWindow = 0.10f;
        int[] masks = { MaskHp, MaskHp };
        float voidUntil; int hits, m1Hits; float lastHp = -1;
        public bool VoidOn { get { return Time.time < voidUntil; } }
        Skill lastWhite = Skill.None;
        bool diving, wasGrounded, dumped;
        float tAtk = -9, tDash = -9, tSkill = -9;

        void Awake() { Inst = this; }

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
                Plugin.Log("input actions found. members: " + string.Join(", ", R.MemberNames(acts).ToArray()));
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

                if (skillNow && down) White(Skill.Moonfall, hero);                       // Skill + Down (air)
                else if (skillNow && up) White(Skill.LunarRise, hero);                   // Skill + Up
                else if ((skillNow || atkNow) && nearSA) White(Skill.CrescentCut, hero); // Skill + Attack
                else if ((dashNow || atkNow) && nearDA) Black(Skill.VoidRend, hero);     // Dash + Attack
                else if ((skillNow || dashNow) && nearSD) Black(Skill.UmbralVeil, hero); // Skill + Dash
                else if (castHeld && down && atkNow) Black(Skill.EventHorizon, hero);    // Hold Skill + Down, then Attack

                bool g = Grounded(hero);
                if (diving && g && !wasGrounded) Land(hero);
                wasGrounded = g;
            }
            catch (Exception e) { Plugin.Warn("update: " + e.Message); }
        }

        // ----- Eclipse Bind: damage is soaked by 2 masks (2 HP each); healing refills them -----
        void TickMasks(object hero)
        {
            var pd = R.PlayerData(hero); if (pd == null) return;
            int hp = R.Get<int>(pd, "health");
            if (lastHp >= 0)
            {
                int last = (int)lastHp;
                if (hp < last)
                {
                    int drop = last - hp, soaked = 0;
                    for (int m = 0; m < masks.Length && drop > 0; m++) { int t = Mathf.Min(drop, masks[m]); masks[m] -= t; drop -= t; soaked += t; }
                    if (soaked > 0) { R.CallWarn(hero, "AddHealth", soaked); hp += soaked; Plugin.Log("masks soaked " + soaked + " (left " + masks[0] + "," + masks[1] + ")"); }
                }
                else if (hp > last) RefillMasks(hp - last);
            }
            lastHp = hp;
        }
        void RefillMasks(int amount)
        {
            for (int m = 0; m < masks.Length && amount > 0; m++)
            { int add = Mathf.Min(amount, MaskHp - masks[m]); if (add > 0) { masks[m] += add; amount -= add; } }
        }

        // ----- Moon skills (white): no cooldown, never the same one twice in a row -----
        void White(Skill s, object hero)
        {
            if (s == lastWhite) return;
            float f = Facing(hero);
            switch (s)
            {
                case Skill.Moonfall:
                    if (Grounded(hero)) return;
                    diving = true; Vel(hero, 0, -45f); Invuln(hero, 0.2f); break;
                case Skill.LunarRise:
                    Vel(hero, 0, 22f); Fx.Play("MoonWhirl", P(hero) + new Vector3(0, 1.8f, 0), 6f, false, 30f);
                    HitAt(hero, P(hero) + new Vector3(0, 2.5f, 0), 3f, 1.1f, false); break;
                case Skill.CrescentCut:
                    if (!TakeSilk(hero, CutSilk)) return;
                    Fx.Play("ThreadLunge", P(hero) + new Vector3(f * 3f, 0, 0), 7f, false, 36f);
                    HitAt(hero, P(hero) + new Vector3(f * 3.5f, 0, 0), 3f, 1.2f, false); break;
            }
            Plugin.Log("moon skill: " + s);
            lastWhite = s;
        }
        void Land(object hero)
        {
            diving = false;
            Fx.Play("MeteorShock", P(hero) + new Vector3(0, -0.6f, 0), 9f, false, 24f);
            if (HitAt(hero, P(hero), 4f, 1.2f, false) > 0) R.CallWarn(hero, "AddHealth", 1);   // heal only if the slam connects
        }

        // ----- Eclipse skills (black): weaken damage for a while -----
        void Black(Skill s, object hero)
        {
            switch (s)
            {
                case Skill.VoidRend:
                    if (!TakeSilk(hero, RendSilk)) return;
                    Fx.Play("VoidTendrils", P(hero), 9f, true, 14f); HitAt(hero, P(hero), 4.5f, 1.4f, false); break;
                case Skill.UmbralVeil:
                    Invuln(hero, 1.2f); Fx.Play("UmbralBurst", P(hero), 6f, true, 12f); break;
                case Skill.EventHorizon:
                    if (!TakeSilk(hero, HorizonSilk)) return;
                    Fx.Play("UmbralBurst", P(hero), 11f, false, 20f); HitAt(hero, P(hero), 7f, 2.0f, true); break;
            }
            Plugin.Log("eclipse skill: " + s);
            voidUntil = Time.time + VoidTime; lastWhite = Skill.None;
        }

        // ----- called from the nail-hit patch: custom M1 flavour -----
        public void OnNailHit()
        {
            var hero = R.Hero(); if (hero == null) return;
            if (++hits % EchoEvery == 0) AddSilk(hero, 1);                                   // Silk Echo
            if (++m1Hits % 3 == 0)                                                           // every 3rd M1 throws a crescent
            {
                float f = Facing(hero);
                Fx.Play("ThreadLunge", P(hero) + new Vector3(f * 2.5f, 0, 0), 4f, false, 36f);
                HitAt(hero, P(hero) + new Vector3(f * 3f, 0, 0), 2f, 0.7f, false);
            }
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
        public void AddSilk(object h, int n) { R.CallWarn(h, "AddSilk", n, false); }
        int HitAt(object hero, Vector3 pos, float radius, float mult, bool pull)
        {
            var pd = R.PlayerData(hero);
            bool full = R.Get<int>(pd, "health") >= R.Get<int>(pd, "maxHealth") && R.Get<int>(pd, "maxHealth") > 0;
            float m = mult * (full ? MoonlitMult : 1f) * (VoidOn ? VoidMult : 1f);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(5 * m)), n = 0;
            var p2d = R.T("UnityEngine.Physics2D"); var hmT = R.T("HealthManager");
            if (p2d == null || hmT == null) return 0;
            var mi = p2d.GetMethod("OverlapCircleAll", new[] { typeof(Vector2), typeof(float) });
            if (mi == null) return 0;
            var res = mi.Invoke(null, new object[] { new Vector2(pos.x, pos.y), radius }) as Array;
            if (res == null) return 0;
            Vector3 hp3 = P(hero);
            foreach (var o in res)
            {
                var c = o as Component; if (c == null) continue;
                var hm = c.GetComponent(hmT); if (hm == null) continue;
                if (pull)                                           // Event Horizon drags enemies toward you first
                {
                    Vector3 d = hp3 - c.transform.position; d.z = 0;
                    float step = Mathf.Min(d.magnitude - 1.5f, 4f);
                    if (step > 0) c.transform.position += d.normalized * step;
                }
                if (R.Call(hm, "ApplyExtraDamage", dmg)) n++;
            }
            return n;
        }
    }

    // ---------- nail-hit patch body ----------
    public static class PatchFns
    {
        // Moonlit Edge (+15% at full health), void penalty (-25%), Silk Echo, crescent every 3rd hit
        public static void NailPrefix(object[] __args)
        {
            if (!Boot.On || __args == null || __args.Length == 0) return;
            try
            {
                var hi = __args[0]; if (hi == null) return;
                var at = R.Member(hi, "AttackType");
                if (at == null || at.ToString() != "Nail") return;
                float d = R.Get<int>(hi, "DamageDealt");
                var pd = R.PlayerData(R.Hero());
                if (R.Get<int>(pd, "health") >= R.Get<int>(pd, "maxHealth")) d *= 1.15f;
                if (Eclipse.Inst != null && Eclipse.Inst.VoidOn) d *= 0.75f;
                R.SetMember(hi, "DamageDealt", Mathf.Max(1, Mathf.RoundToInt(d)));
                __args[0] = hi;
                if (Eclipse.Inst != null) Eclipse.Inst.OnNailHit();
            }
            catch (Exception e) { Plugin.Warn("nail patch: " + e.Message); }
        }
    }
}
