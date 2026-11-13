using UnityEngine;
using Unity.Barracuda;
using System.Collections.Generic;

public class ChessAI : MonoBehaviour
{
    // The trained model in ONNX format
    public NNModel modelAsset;

    private Model runtimeModel;
    private IWorker worker;

    void Start()
    {
        if (modelAsset != null)
        {
            // 1. Load the model
            runtimeModel = ModelLoader.Load(modelAsset);

            // 2. Create the inference engine (Worker)
            worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
        }
        else
        {
            Debug.LogWarning("No ONNX Model assigned to ChessAI!");
        }
    }

    /// <summary>
    /// Generates a move using the ML model.
    /// Input tensor is float[1, 8, 8, 12].
    /// </summary>
    // --- FIX: Changed from GameManager.InternalPiece to MinimaxAI.InternalPiece ---
    public void GetBestMove(MinimaxAI.InternalPiece[,] board)
    {
        if (worker == null) return;

        // --- A. PRE-PROCESSING ---
        Tensor inputTensor = ConvertBoardToTensor(board);

        // --- B. INFERENCE ---
        worker.Execute(inputTensor);

        // --- C. POST-PROCESSING ---
        Tensor outputTensor = worker.PeekOutput();

        // Convert the output tensor to a float array
        float[] probabilities = outputTensor.AsFloats();

        // Clean up tensors immediately to free memory
        inputTensor.Dispose();
        outputTensor.Dispose();

        // TODO: You will need to write the logic here to pick the best move 
        // from the 'probabilities' array and send it back to GameManager.
        Debug.Log($"Model Inference Complete. Output size: {probabilities.Length}");
    }

    // --- FIX: Changed from GameManager.InternalPiece to MinimaxAI.InternalPiece ---
    private Tensor ConvertBoardToTensor(MinimaxAI.InternalPiece[,] board)
    {
        // Create a tensor of shape (Batch: 1, Height: 8, Width: 8, Channels: 12)
        Tensor tensor = new Tensor(1, 8, 8, 12);

        // Loop through the board and fill the channels based on piece type
        // This is a placeholder loop - you need to match your specific model's channel order.
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                // Example: Fill tensor based on board[r,c]
                // tensor[0, r, c, channelIndex] = 1.0f;
            }
        }

        return tensor;
    }

    void OnDestroy()
    {
        // MUST manually dispose of the worker to prevent memory leaks
        worker?.Dispose();
    }
}