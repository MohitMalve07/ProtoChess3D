using UnityEngine;
using UnityEngine.UI;

public class GameModeSelector : MonoBehaviour
{
    // Drag your GameManager object here in the Inspector
    public GameManager gameManager;

    [Header("Menu Panels")]
    public GameObject mainPanel;
    public GameObject buttonsContainer;
    public GameObject howToPlayPanel;
    public GameObject shadowsPanel;

    [Header("Shadows Toggle")]
    public Toggle shadowsToggle;
    public Image shadowsToggleImage;
    public Sprite toggleOffSprite;
    public Sprite toggleOnSprite;
    public Light directionalLight;

    void Start()
    {
        // Find the GameManager if not assigned
        if (gameManager == null)
        {
            gameManager = FindAnyObjectByType<GameManager>();
        }

        if (mainPanel == null) mainPanel = transform.Find("Panel")?.gameObject;
        if (buttonsContainer == null) buttonsContainer = transform.Find("Buttons")?.gameObject;
        if (howToPlayPanel == null) howToPlayPanel = transform.Find("How to Play")?.gameObject;
        if (shadowsPanel == null) shadowsPanel = transform.Find("Shadows Panel")?.gameObject;

        if (shadowsToggle == null && shadowsPanel != null)
        {
            shadowsToggle = shadowsPanel.GetComponentInChildren<Toggle>(true);
        }

        if (shadowsToggleImage == null && shadowsToggle != null)
        {
            shadowsToggleImage = shadowsToggle.targetGraphic as Image ?? shadowsToggle.GetComponentInChildren<Image>(true);
        }

        if (directionalLight == null)
        {
            foreach (Light l in FindObjectsByType<Light>())
            {
                if (l.type == LightType.Directional)
                {
                    directionalLight = l;
                    break;
                }
            }
        }

        if (shadowsToggle != null)
        {
            shadowsToggle.SetIsOnWithoutNotify(false);
        }
        ApplyShadowState(false);

        CloseHowToPlay();

        // Ensure the game is paused and cursor is visible at the start
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void SelectPlayerVsPlayer()
    {
        if (gameManager != null)
        {
            gameManager.StartGame(0); // 0 corresponds to PvP
            ResumeGame();
        }
    }

    public void SelectPlayerVsAI()
    {
        if (gameManager != null)
        {
            gameManager.StartGame(1); // 1 corresponds to PvAI
            ResumeGame();
        }
    }

    public void OpenHowToPlay()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (buttonsContainer != null) buttonsContainer.SetActive(false);
        if (shadowsPanel != null) shadowsPanel.SetActive(false);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(true);
    }

    public void CloseHowToPlay()
    {
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
        if (buttonsContainer != null) buttonsContainer.SetActive(true);
        if (shadowsPanel != null) shadowsPanel.SetActive(true);
    }

    public void OnShadowsToggleChanged(bool isOn)
    {
        ApplyShadowState(isOn);
    }

    private void ApplyShadowState(bool isOn)
    {
        if (shadowsToggleImage != null)
        {
            Sprite targetSprite = isOn ? toggleOnSprite : toggleOffSprite;
            if (targetSprite != null)
            {
                shadowsToggleImage.sprite = targetSprite;
            }
        }

        if (directionalLight != null)
        {
            directionalLight.shadows = isOn ? LightShadows.Soft : LightShadows.None;
        }
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ResumeGame()
    {
        // Hide this canvas
        gameObject.SetActive(false);
        // Resume game time
        Time.timeScale = 1f;
    }
}