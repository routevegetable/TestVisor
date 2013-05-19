using System;
using ServiceStack.Redis;
using System.IO;

namespace Bizarrefish.VMTestLib
{
	internal class RedisTestResource : ITestResource
	{
		IRedisClient client;
		string hashKey, name;

		public RedisTestResource(IRedisClient client, string name, string hashKey)
		{
			this.client = client;
			this.hashKey = hashKey;
			this.name = name;
		}

		Stream OpenWithFunction(Func<string, Stream> func)
		{
			string fileName = client.GetValueFromHash(hashKey, name);
			if(fileName == null)
			{
				throw new Exception("Resource: " + name + " has been removed from the index");
			}
			return func(fileName);
		}

		public System.IO.Stream Read ()
		{
			return OpenWithFunction(File.OpenRead);
		}

		public void Write (System.IO.Stream s)
		{
			using(Stream fileStream = OpenWithFunction(File.OpenWrite))
			{
				s.CopyTo(fileStream);
			}
		}

		public System.IO.Stream Write ()
		{
			return OpenWithFunction (File.OpenWrite);
		}
	}

	public class RedisTestRepository : ITestRepository
	{
		IRedisClient client;

		// The blob
		string blobKey;

		// A hash mapping resource names to file names
		string resourceIndexKey;

		// To generate filenames for our resources
		string resourceCounterKey;

		// A lock, for when we're modifying the index
		string lockKey;

		// The directory resources are kept in.
		string resourceDirectory;

		public RedisTestRepository (IRedisClient client, string filePrefix, string dbPrefix)
		{
			this.client = client;
			this.client = new RedisClient("localhost");
			blobKey = dbPrefix + "/Blob";
			resourceIndexKey = dbPrefix + "/ResourceFileIndex";
			resourceCounterKey = dbPrefix + "/ResourceCounter";
			lockKey = dbPrefix + "/IndexLock";
			resourceDirectory = filePrefix;

			CheckIndex();
		}

		
		public System.Collections.Generic.IEnumerable<string> Resources {
			get
			{
				return client.GetHashKeys(resourceIndexKey);
			}
		}

		void CheckIndex()
		{
			WithIndexLock (() => 
			{
				var map = client.GetAllEntriesFromHash(resourceIndexKey);

				foreach(var entry in map)
				{
					if(!File.Exists(entry.Value))
					{
						Console.Error.WriteLine("Resource: " + entry.Key + " doesn't exist; Deleting from index.");
						client.RemoveEntryFromHash(resourceIndexKey, entry.Key);
					}
				}
			});
		}


		public void Store<TBlob> (TBlob blob)
		{
			client.Set(blobKey, blob);
		}

		public TBlob Load<TBlob> () where TBlob : new ()
		{
			return client.Get<TBlob>(blobKey);
		}

		void WithIndexLock(Action act)
		{
			using(var l = client.AcquireLock(lockKey))
			{
				act();
			}
		}

		public ITestResource CreateResource (string name)
		{
			string fileName = null;

			WithIndexLock (() => 
			{
				long ctr = client.IncrementValue(resourceCounterKey);
				fileName = resourceDirectory + "/" + ctr + ".resource";

				client.SetEntryInHash(resourceIndexKey, name, fileName);

			});

			File.Create(fileName).Close ();

			return new RedisTestResource(client, name, resourceIndexKey);
		}

		public ITestResource GetResource (string name)
		{
			var fileName = client.GetValueFromHash(resourceIndexKey, name);
			if(fileName != null)
			{
				return new RedisTestResource(client, name, resourceIndexKey);
			}
			else
			{
				throw new Exception("Resource: " + name + " doesn't exist");
			}
		}

		public void DeleteResource (string name)
		{
			string fileName = null;

			WithIndexLock (() => 
			{
				fileName = client.GetValueFromHash(resourceIndexKey, name);
				client.RemoveEntryFromHash(resourceIndexKey, name);
			});

			File.Delete (fileName);
		}
	}
}

