namespace Hoshi.Core.Localization;

public static partial class Tr
{
    static partial void AddEngines(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Engine.ExecutableMissing", "KataGo's executable was not found.", "No se encuentra el ejecutable de KataGo.");
        Add(d, "Engine.ModelMissing", "KataGo's neural network (.bin.gz) was not found.", "No se encuentra la red neuronal (.bin.gz) de KataGo.");
        Add(d, "Engine.ConfigMissing", "The analysis configuration file (analysis_example.cfg) was not found.", "No se encuentra el archivo de configuración de análisis (analysis_example.cfg).");
        Add(d, "Engine.NotAnalysisConfig",
            "«{0}» is not an analysis configuration (it is for GTP). Choose analysis_example.cfg, in the same folder as KataGo.",
            "«{0}» no es una configuración de análisis (es para GTP). Elige analysis_example.cfg, en la misma carpeta de KataGo.");
        Add(d, "Engine.DidNotStart", "KataGo did not start.", "KataGo no arrancó.");
        Add(d, "Engine.CouldNotStart", "Could not start KataGo: {0}", "No se pudo iniciar KataGo: {0}");
        Add(d, "Engine.Tuning",
            "KataGo is tuning the graphics card. This only happens the first time and can take several minutes",
            "KataGo está calibrando la tarjeta gráfica. Solo pasa la primera vez y puede tardar varios minutos");
        Add(d, "Engine.TuningStep", "{0} (step {1})…", "{0} (paso {1})…");
        Add(d, "Engine.LoadingNetwork", "KataGo is loading the neural network…", "KataGo está cargando la red neuronal…");
        Add(d, "Engine.Hint.MissingDll",
            "a DLL is missing. KataGo's CUDA and TensorRT builds need CUDA, cuDNN or TensorRT installed; if you don't have them, use the OpenCL build (or Eigen, CPU only).",
            "falta una DLL. Las versiones CUDA y TensorRT de KataGo necesitan CUDA, cuDNN o TensorRT instalados; si no los tienes, usa la versión OpenCL (o Eigen, solo CPU).");
        Add(d, "Engine.Hint.IllegalInstruction",
            "your processor does not support the instructions of this KataGo build. Use the Eigen build without AVX2.",
            "tu procesador no admite las instrucciones de esta versión de KataGo. Usa la versión Eigen sin AVX2.");
        Add(d, "Engine.Hint.AccessViolation",
            "KataGo crashed inside the graphics card driver. Update the driver or use the Eigen build (CPU only).",
            "KataGo falló dentro del controlador de la tarjeta gráfica. Actualiza el controlador o usa la versión Eigen (solo CPU).");
        Add(d, "Engine.Hint.Aborted",
            "KataGo aborted. Check that the neural network is compatible with your KataGo version.",
            "KataGo abortó. Revisa que la red neuronal sea compatible con tu versión de KataGo.");
        Add(d, "Engine.ExitCode", "code {0}: {1}", "código {0}: {1}");
        Add(d, "Engine.ExitCodeNoMessage", "code {0}, no message from KataGo.", "código {0}, sin mensaje de KataGo.");
        Add(d, "Engine.Exited", "KataGo closed ({0})", "KataGo se cerró ({0})");
        Add(d, "Engine.ExitedUnexpectedly", "KataGo closed unexpectedly. Check its configuration and the log.", "KataGo se cerró inesperadamente. Revisa su configuración y el log.");
    }
}
