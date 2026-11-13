using UnityEngine;

public class GameModeSelector : MonoBehaviour
{
    // Drag your GameManager object here in the Inspector
    public GameManager gameManager;

    void Start()
    {
        // Find the GameManager if not assigned
        if (gameManager == null)
        {
            gameManager = FindObjectOfType<GameManager>();
        }

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

    private void ResumeGame()
    {
        // Hide this canvas
        gameObject.SetActive(false);
        // Resume game time
        Time.timeScale = 1f;
    }
}