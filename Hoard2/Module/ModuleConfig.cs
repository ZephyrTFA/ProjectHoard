using System.Runtime.Serialization;

namespace Hoard2.Module
{
	public class ModuleConfig
	{
		public ModuleConfig(string filePath)
		{
			StoreInfo = new FileInfo(filePath);
			if (StoreInfo.Exists)
				Read();
		}

		FileInfo StoreInfo { get; }

		Dictionary<string, object?> _configData = new Dictionary<string, object?>();

		public T? Get<T>(string key, T? defaultValue = default) => Has(key) ? (T?)_configData[key] : defaultValue;

		public bool Has(string key) => _configData.ContainsKey(key);

		public void Clear()
		{
			_configData.Clear();
			Save();
		}

		public void Set<T>(string key, T setValue)
		{
			_configData[key] = setValue;
			Save();
		}

		public void Remove(string key)
		{
			if (!Has(key)) return;
			_configData.Remove(key);
			Save();
		}

		public void Save()
		{
			if (StoreInfo.Exists)
				StoreInfo.Delete();
			using var writer = StoreInfo.Create();
			var binary = new DataContractSerializer(_configData.GetType());
			binary.WriteObject(writer, _configData);
			writer.Dispose();
		}

		public void Read()
		{
			if (!StoreInfo.Exists)
				return;
			using var reader = StoreInfo.OpenRead();
			var binary = new DataContractSerializer(_configData.GetType());
			var read = binary.ReadObject(reader);
			_configData = (Dictionary<string, object?>?)read ?? throw new IOException($"Failed to read config for {GetType().FullName}");
		}
	}
}
