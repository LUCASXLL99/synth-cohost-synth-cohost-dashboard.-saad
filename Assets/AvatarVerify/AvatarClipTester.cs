using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class AvatarClipTester : MonoBehaviour
{
    [Tooltip("Animator state names, matching the controller.")]
    public string[] stateNames = new string[0];

    public int currentIndex;

    [Tooltip("Clips whose FBX hierarchy does not match the character.")]
    public string[] expectedBrokenStateNames = new string[0];

    public event Action<int> ClipChanged;

    Animator _animator;
    bool _loopCurrent;

    public Animator Animator
    {
        get { return _animator; }
    }

    public string CurrentStateName
    {
        get
        {
            if (stateNames == null || stateNames.Length == 0)
                return string.Empty;
            return stateNames[Mathf.Clamp(currentIndex, 0, stateNames.Length - 1)];
        }
    }

    public bool IsCurrentBroken
    {
        get { return IsExpectedBroken(CurrentStateName); }
    }

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    void Start()
    {
        int idle = IndexOfPrefix("01_Idle_A");
        PlayIndex(idle >= 0 ? idle : 0);
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.rightArrowKey.wasPressedThisFrame || kb.nKey.wasPressedThisFrame)
                PlayIndex(currentIndex + 1);
            if (kb.leftArrowKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame)
                PlayIndex(currentIndex - 1);
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)
                PlayNamedPrefix("01_Idle_A");
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)
                PlayNamedPrefix("04_Walk");
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame)
                PlayNamedPrefix("05_Run");
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame)
                PlayNamedPrefix("08_jump");
        }

        if (_animator == null || !_loopCurrent)
            return;

        AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
        if (info.normalizedTime >= 1f)
            _animator.Play(stateNames[currentIndex], 0, 0f);
    }

    public void PlayIndex(int index)
    {
        if (stateNames == null || stateNames.Length == 0 || _animator == null)
            return;

        currentIndex = (index % stateNames.Length + stateNames.Length) % stateNames.Length;
        _loopCurrent = ShouldLoop(stateNames[currentIndex]);
        _animator.Play(stateNames[currentIndex], 0, 0f);
        if (ClipChanged != null)
            ClipChanged(currentIndex);
    }

    public void PlayNamedPrefix(string prefix)
    {
        int i = IndexOfPrefix(prefix);
        if (i >= 0)
            PlayIndex(i);
    }

    public void Replay()
    {
        PlayIndex(currentIndex);
    }

    public bool IsExpectedBroken(string stateName)
    {
        if (expectedBrokenStateNames == null || string.IsNullOrEmpty(stateName))
            return false;
        for (int i = 0; i < expectedBrokenStateNames.Length; i++)
        {
            if (expectedBrokenStateNames[i] == stateName)
                return true;
        }
        return false;
    }

    int IndexOfPrefix(string prefix)
    {
        if (stateNames == null)
            return -1;
        for (int i = 0; i < stateNames.Length; i++)
        {
            if (stateNames[i].StartsWith(prefix))
                return i;
        }
        return -1;
    }

    static bool ShouldLoop(string stateName)
    {
        string n = stateName.ToLowerInvariant();
        return n.Contains("idle")
            || n.Contains("walk")
            || n.Contains("run")
            || n.Contains("hang")
            || n.Contains("sleep_loop")
            || n.Contains("listening")
            || n.Contains("breathing");
    }
}
