using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace Hoard2.Module
{
	public class ModuleConfig
	{
		public static readonly Type[] KnownTypes =
		{
			typeof(Dictionary<string, string>),
			typeof(Dictionary<ulong, string>),
		};

		Dictionary<string, object> _configData = new Dictionary<string, object>();
		public ModuleConfig(string filePath)
		{
			StoreInfo = new FileInfo(Path.GetFullPath(filePath));
			if (!StoreInfo.Directory!.Exists)
				StoreInfo.Directory.Create();

			if (StoreInfo.Exists)
				Read();
		}

		FileInfo StoreInfo { get; }

		public T? Get<T>(string key, T? defaultValue = default)
		{
			if (Has(key)) return (T?)_configData[key];
			return defaultValue;
		}

		public bool TryGet<T>(string key, [NotNullWhen(true)] out T? value)
		{
			value = Get<T>(key);
			return value is not null;
		}

		public bool Has(string key) => _configData.ContainsKey(key);

		public void Clear()
		{
			_configData.Clear();
			Save();
		}

		public void Set<T>(string key, T setValue)
		{
			if (setValue is null) throw new NullReferenceException();
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
			var memory = new MemoryStream();
			var binary = new DataContractSerializer(typeof(Dictionary<string, object>), KnownTypes);
			binary.WriteObject(memory, _configData);

			if (StoreInfo.Exists)
				StoreInfo.Delete();
			using var writer = StoreInfo.Create();
			memory.Seek(0, SeekOrigin.Begin);
			memory.WriteTo(writer);
		}

		public void Read()
		{
			if (!StoreInfo.Exists)
				return;
			using var reader = StoreInfo.OpenRead();
			var binary = new DataContractSerializer(_configData.GetType(), KnownTypes);
			var read = binary.ReadObject(reader);
			_configData = (Dictionary<string, object>?)read ?? throw new IOException($"Failed to read config for {GetType().FullName}");
		}
	}
}
