using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class DialougeManager : MonoBehaviour
{
    public static DialougeManager main;

    [Header("UI")]
    public RectTransform dialougeUI;            // root of the dialogue UI (used for scale animation)
    public CanvasGroup canvasGroup;             // used for fade in/out
    public Image speakerImage;
    public TMP_Text speakerName;
    public TMP_Text dialougeText;

    [Header("Choices")]
    public Transform choicesContainer;          // parent to spawn choice buttons under
    public Button choiceButtonPrefab;           // assign a prefab with a Button and a TMP_Text child (fallback created if null)

    [Header("Animation")]
    public float fadeDuration = 0.18f;
    public float scaleDuration = 0.18f;
    public Vector3 hiddenScale = new Vector3(0.9f, 0.9f, 0.9f);
    public Vector3 shownScale = Vector3.one;

    [Header("Typing")]

    // Runtime
    private Coroutine _currentRoutine;
    private DialougeTextObject _currentAsset;
    private int _lineIndex;
    private bool _isTyping;
    private bool _skipTyping;

    void Awake()
    {
        main = this;
        // ensure canvasGroup is present
        if (canvasGroup == null && dialougeUI != null)
        {
            canvasGroup = dialougeUI.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = dialougeUI.gameObject.AddComponent<CanvasGroup>();
        }
    }

    void Start()
    {
        // ensure hidden state
        if (dialougeUI != null)
        {
            dialougeUI.localScale = hiddenScale;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }

    // Public API: feed a single ScriptableObject asset to show full dialogue
    public void ShowDialouge(DialougeTextObject asset)
    {
        if (asset == null || asset.lines == null || asset.lines.Count == 0) return;

        // stop any running
        if (_currentRoutine != null) StopCoroutine(_currentRoutine);
        _currentAsset = asset;
        _lineIndex = 0;
        _currentRoutine = StartCoroutine(RunDialouge());
    }

    public void HideDialouge()
    {
        if (_currentRoutine != null) StopCoroutine(_currentRoutine);
        _currentAsset = null;
        ClearChoices();
        _currentRoutine = StartCoroutine(HideRoutine());
    }

    private IEnumerator RunDialouge()
    {
        yield return ShowRoutine();

        while (_currentAsset != null && _lineIndex < _currentAsset.lines.Count)
        {
            var line = _currentAsset.lines[_lineIndex];

            // call start event
            line.onLineStart?.Invoke();

            // set speaker UI
            speakerName.text = line.speakerName ?? "";
            speakerImage.sprite = line.speakerSprite;

            // type text
            yield return StartCoroutine(TypeLine(line.text, line.typeSFX, line.typeSpeed));

            // call end event
            line.onLineEnd?.Invoke();

            // if there are choices, present them and wait until a choice triggers next
            if (line.choices != null && line.choices.Count > 0)
            {
                yield return PresentChoices(line.choices);
                // PresentChoices sets _currentAsset/_lineIndex if branching used; if current asset became null, break
                if (_currentAsset == null) break;
                // otherwise continue from updated _lineIndex
                continue;
            }

            // wait for input if requested
            if (line.waitForInput)
            {
                yield return WaitForContinue();
            }
            else
            {
                // small default delay before next line so it doesn't feel instant
                yield return new WaitForSeconds(0.35f);
            }

            _lineIndex++;
        }

        // finished
        HideDialouge();
    }

    private IEnumerator TypeLine(string text, AudioClip typeSfx = null, float charDelay = 0.02f)
    {
        _isTyping = true;
        _skipTyping = false;
        dialougeText.text = string.Empty;

        if (string.IsNullOrEmpty(text))
        {
            _isTyping = false;
            yield break;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (_skipTyping) break;

            dialougeText.text += text[i];
            if (!char.IsSeparator(text[i]))
                AudioManager.PlaySound(typeSfx, "SFX", Camera.main.transform.position, 0.5f);

            yield return new WaitForSeconds(charDelay);
        }

        if (_skipTyping)
        {
            dialougeText.text = text;
            _skipTyping = false;
        }

        _isTyping = false;
    }

    // call this (e.g. via UI button or click) to skip current typing or continue
    public void OnClickAdvance()
    {
        if (_isTyping)
        {
            _skipTyping = true;
            return;
        }
        // otherwise signal continue by doing nothing; the waiting coroutine polls input (WaitForContinue)
    }

    private IEnumerator WaitForContinue()
    {
        // show a subtle pulse or keep UI interactive; here we wait for mouse or keyboard
        while (true)
        {
            if (InputHandler.GetKeyDown(KeyCode.Mouse0, 1) || InputHandler.GetKeyDown(KeyCode.Space, 1) || InputHandler.GetKeyDown(KeyCode.Return, 1))
                yield break;
            yield return null;
        }
    }

    private IEnumerator PresentChoices(List<DialougeTextObject.DialougeChoice> choices)
    {
        ClearChoices();

        // create buttons
        for (int i = 0; i < choices.Count; i++)
        {
            var ch = choices[i];
            Button btn = CreateChoiceButton(ch.choiceText);
            int idx = i;
            btn.onClick.AddListener(() =>
            {
                // invoke choice event
                ch.onSelect?.Invoke();

                // branching: if a next dialouge is provided, start it immediately.
                if (ch.nextDialouge != null)
                {
                    // stop current run and start next
                    _currentAsset = ch.nextDialouge;
                    _lineIndex = 0;
                    // cleanup choices and let RunDialouge loop continue
                    ClearChoices();
                    // stop any typing
                    _isTyping = false;
                    _skipTyping = false;
                }
                else
                {
                    // otherwise just continue to next line
                    _lineIndex++;
                    ClearChoices();
                }
            });
        }

        // Wait until choices cleared (either one clicked will call ClearChoices)
        while (choicesContainer != null && choicesContainer.childCount > 0)
        {
            yield return null;
        }
    }

    private Button CreateChoiceButton(string label)
    {
        // if user provided a prefab, use it
        if (choiceButtonPrefab != null)
        {
            var go = Instantiate(choiceButtonPrefab, choicesContainer);
            // try to find a TMP_Text child
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = label;
            else
            {
                // create one
                var textGO = new GameObject("Label", typeof(RectTransform));
                textGO.transform.SetParent(go.transform, false);
                var tmpComp = textGO.AddComponent<TMP_Text>();
                tmpComp.text = label;
                tmpComp.fontSize = 24;
                tmpComp.alignment = TextAlignmentOptions.Center;
                var rt = textGO.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            return go;
        }

        // fallback: create a simple button + TMP_Text programmatically
        var btnGO = new GameObject("ChoiceButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(choicesContainer, false);
        var image = btnGO.GetComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);

        var button = btnGO.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;

        var txtGO = new GameObject("Label", typeof(RectTransform));
        txtGO.transform.SetParent(btnGO.transform, false);
        var tmpComp2 = txtGO.AddComponent<TMP_Text>();
        tmpComp2.text = label;
        tmpComp2.fontSize = 22;
        tmpComp2.alignment = TextAlignmentOptions.Center;

        var rtBtn = btnGO.GetComponent<RectTransform>();
        rtBtn.sizeDelta = new Vector2(380, 40);

        var rtTxt = txtGO.GetComponent<RectTransform>();
        rtTxt.anchorMin = Vector2.zero;
        rtTxt.anchorMax = Vector2.one;
        rtTxt.offsetMin = Vector2.zero;
        rtTxt.offsetMax = Vector2.zero;

        return button;
    }

    private void ClearChoices()
    {
        if (choicesContainer == null) return;
        for (int i = choicesContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(choicesContainer.GetChild(i).gameObject);
        }
    }

    private IEnumerator ShowRoutine()
    {
        if (dialougeUI == null || canvasGroup == null)
            yield break;

        canvasGroup.blocksRaycasts = true;

        float t = 0f;
        float duration = Mathf.Max(fadeDuration, scaleDuration);
        Vector3 startScale = hiddenScale;
        Vector3 endScale = shownScale;
        float startAlpha = canvasGroup.alpha;
        InputHandler.AddInputModifier(InputModifier.CreateInputModifier("Dialouge", true, 0));

        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            // ease out
            float ease = 1f - Mathf.Pow(1f - frac, 3f);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, ease);
            dialougeUI.localScale = Vector3.Lerp(startScale, endScale, ease);
            yield return null;
        }
        canvasGroup.alpha = 1f;
        dialougeUI.localScale = endScale;
    }

    private IEnumerator HideRoutine()
    {
        InputHandler.RemoveInputModifier("Dialouge");
        if (dialougeUI == null || canvasGroup == null)
            yield break;

        canvasGroup.blocksRaycasts = false;

        float t = 0f;
        float duration = Mathf.Max(fadeDuration, scaleDuration);
        Vector3 startScale = dialougeUI.localScale;
        Vector3 endScale = hiddenScale;
        float startAlpha = canvasGroup.alpha;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            // ease in
            float ease = Mathf.Pow(1f - frac, 2f);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, 1f - ease);
            dialougeUI.localScale = Vector3.Lerp(startScale, endScale, 1f - ease);
            yield return null;
        }
        canvasGroup.alpha = 0f;
        dialougeUI.localScale = endScale;

        // clear text & choices
        dialougeText.text = string.Empty;
        ClearChoices();
    }
}
