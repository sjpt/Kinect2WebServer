using Orbbec;

namespace Samples.Preset
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Create a pipeline with default device.
                using var pipe = new Pipeline();

                // Get the device from the pipeline.
                using var device = pipe.GetDevice();

                while (true)
                {
                    // Get preset list from device.
                    using var presetLists = device.GetAvailablePresetList();
                    if (presetLists == null || presetLists.Count() == 0)
                    {
                        Console.WriteLine("The current device does not support preset mode");
                        Console.WriteLine("\nPress any key to exit.");
                        Console.ReadKey(true);
                        return;
                    }

                    Console.WriteLine("Available Presets:");
                    for (uint index = 0; index < presetLists.Count(); index++)
                    {
                        // Print available preset name.
                        Console.WriteLine($" - {index}.{presetLists.GetName(index)}");
                    }

                    // Print current preset name.
                    Console.WriteLine($"Current PresetName: {device.GetCurrentPresetName()}");

                    Console.Write("Enter index of preset to load: ");

                    // Select preset to load.
                    string? input = Console.ReadLine();
                    if (!int.TryParse(input, out int inputOption))
                    {
                        continue;
                    }

                    // Exit on -1
                    if (inputOption == -1)
                    {
                        break;
                    }

                    if (inputOption < 0 || inputOption >= presetLists.Count())
                    {
                        continue;
                    }

                    var presetName = presetLists.GetName((uint)inputOption);

                    // Load preset.
                    device.LoadPreset(presetName);

                    // Print current preset name.
                    Console.WriteLine($"Current PresetName: {device.GetCurrentPresetName()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey(true);
            }
        }
    }
}
