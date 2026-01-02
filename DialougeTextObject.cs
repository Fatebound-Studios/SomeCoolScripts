using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "New Dialouge", menuName = "Dialouge/Dialouge Asset", order = 0)]
public class DialougeTextObject : ScriptableObject
{
    [Serializable]
    public class DialougeChoice
    {
        public string choiceText;
        public UnityEvent onSelect;
        public DialougeTextObject nextDialouge; // optional branching
    }

    [Serializable]
    public class DialougeLine
    {
        public string speakerName;
        public Sprite speakerSprite;
        public AudioClip typeSFX; // sound that plays after each character
        public float typeSpeed = 0.02f; // time between each character appearing
        [TextArea(2, 6)]
        public string text;
        public bool waitForInput = true; // if true, waits for player input before continuing
        public List<DialougeChoice> choices; // if non-empty, choices will be shown instead of auto-continue
        public UnityEvent onLineStart;
        public UnityEvent onLineEnd;
    }

    public List<DialougeLine> lines = new List<DialougeLine>();
}
