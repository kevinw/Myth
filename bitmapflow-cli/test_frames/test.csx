#!/usr/bin/env dotnet-script
#r "../../Foster/Framework/Foster.Framework.csproj"

using Foster.Framework.Images;

var ase = new Aseprite("../TinyLink/Assets/Sprites/floater.ase");
Console.WriteLine($"Frames: {ase.Frames.Count}, Size: {ase.Width}x{ase.Height}");

// Render first 3 frames
for (int i = 0; i < Math.Min(3, ase.Frames.Count); i++)
{
    var img = ase.RenderFrame(i);
    var path = $"frame_{i}.png";
    img.Save(path);
    Console.WriteLine($"Saved {path} - Duration: {ase.Frames[i].Duration}ms");
}
