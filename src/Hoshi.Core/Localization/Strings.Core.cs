namespace Hoshi.Core.Localization;

public static partial class Tr
{
    static partial void AddCore(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Common.Black", "Black", "Negras");
        Add(d, "Game.OpenedFrom", "Opened {0}.", "Abierta {0}.");
        Add(d, "Common.WholeNumberRange", "Enter a whole number from {0} to {1}.", "Escribe un número entero de {0} a {1}.");
        Add(d, "Common.NumberRange", "Enter a number from {0} to {1}.", "Escribe un número de {0} a {1}.");
        Add(d, "Common.White", "White", "Blancas");
        Add(d, "Main.Pass", "Pass", "Pasar");
    }
}
