namespace Myth;

internal static class WordDictionary
{
	const string SysDictionaryName = "/usr/share/dict/words";
	static readonly bool HasSystemDictionary = File.Exists(SysDictionaryName);
	static readonly Random dictRng = new();
	static int DictionaryWordCount = -1;

	internal static string? GetRandomWord()
	{
		if (HasSystemDictionary)
		{
			if (DictionaryWordCount == -1)
			{
				DictionaryWordCount = 0;
				foreach (var _ in File.ReadAllLines(SysDictionaryName))
					DictionaryWordCount++;
			}

			var wordIndex = dictRng.Next(DictionaryWordCount);
			var index = 0;
			foreach (var word in File.ReadAllLines(SysDictionaryName))
			{
				if (wordIndex == index)
					return word;

				index++;
			}
		}

		return null;
	}
}
