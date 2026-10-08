using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ExtraDim
{
    // Build-time signer.
    //   Sign key  <private-key.xml> <public-key-out.xml>   create the key pair once, export the public half
    //   Sign sign <private-key.xml> <ExtraDim.exe>          append an RSA signature of the exe to its end
    // The private key never ships. The app checks the signature on every start (see Integrity.cs).
    static class Sign
    {
        public const string Magic = "EXDMSIG1";

        static int Main(string[] args)
        {
            if (args.Length != 3) { Console.Error.WriteLine("usage: Sign key|sign <private-key.xml> <file>"); return 2; }
            string keyPath = args[1];

            if (args[0] == "key")
            {
                if (!File.Exists(keyPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath)));
                    using (var rsa = new RSACryptoServiceProvider(2048))
                        File.WriteAllText(keyPath, rsa.ToXmlString(true));
                    Console.WriteLine("Created signing key " + keyPath);
                }
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(File.ReadAllText(keyPath));
                    File.WriteAllText(args[2], rsa.ToXmlString(false));
                }
                return 0;
            }

            if (args[0] == "sign")
            {
                byte[] data = File.ReadAllBytes(args[2]);
                byte[] sig;
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(File.ReadAllText(keyPath));
                    sig = rsa.SignData(data, new SHA256CryptoServiceProvider());
                }
                using (var fs = new FileStream(args[2], FileMode.Append))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write(sig);
                    w.Write(sig.Length);
                    w.Write(Encoding.ASCII.GetBytes(Magic));
                }
                Console.WriteLine("Signed " + args[2]);
                return 0;
            }
            return 2;
        }
    }
}
