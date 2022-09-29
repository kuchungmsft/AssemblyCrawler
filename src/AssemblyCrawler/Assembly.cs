using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;


/* NOTES

// Get 


        // Any CPU = x86 + COMIMAGE_FLAGS_ILONLY will give the flag

        (dwManagedImageFlags &COMIMAGE_FLAGS_ILONLY) != 0;


        // How you get the core header

        if (image_optional_header.h32.NumberOfRvaAndSizes > IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR)
        {
            *ManagedCode = image_optional_header.h32.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR].Size ? TRUE : FALSE;
            if (*ManagedCode)
            {
                fSucc = ReadCORHeader(hImage,
                                      image_optional_header.h32.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR].VirtualAddress,
                                      image_optional_header.h32.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR].Size,
                                      image_section_headers,
                                      section_count,
                                      &ich);
            }
        }
*/

namespace AssemblyCrawler
{
    internal class AssemblyInfo
    {
        public readonly Lazy<string> FName;
        public readonly Lazy<string> AName;
        public readonly Lazy<Version> AssemblyVersion;
        public readonly Lazy<Version> FileVersion;
        public readonly Lazy<string> FileVersionString;
        public Lazy<ulong> FileSize;
        public Lazy<string> PublicKeyToken;
        public Lazy<string> FrameworkVersion;

        public Lazy<bool> IsManaged;
        public Lazy<string> MachineType;

        public string Path => file.DirectoryName;
        public string FullPath => file.FullName;
        public bool IsResource => file.IsResource();

        private Lazy<AssemblyName> assemblyName;

        private FileInfo file { get; set; }

        public AssemblyInfo(FileInfo file)
        {
            this.file = file;
            FileVersion = new Lazy<Version>(() => new Version(FileVersionInfo.GetVersionInfo(file.FullName)?.FileVersion?.Split(' ')[0] ?? "0.0.999.9"));
            FileVersionString = new Lazy<string>(() => FileVersionInfo.GetVersionInfo(file.FullName)?.FileVersion ?? "N/A");
            FileSize = new Lazy<ulong>(() => (ulong)file.Length);

            this.assemblyName = new Lazy<AssemblyName>(GetAssemblyName);

            this.FName = new Lazy<string>(() => file.Name.ToLowerInvariant());

            this.AName = new Lazy<string>(() =>
            {
                if (assemblyName.Value is null)
                    return "N/A";

                return assemblyName.Value!.ToString();
            });
            this.AssemblyVersion = new Lazy<Version>(GetAssemblyVersion);
            this.PublicKeyToken = new Lazy<string>(GetPublicKeyToken);
            this.IsManaged = new Lazy<bool>(() => this.IsManagedAssembly());
            this.FrameworkVersion = new Lazy<string>(() => this.InitializeFramework());
            this.MachineType = new Lazy<string>(() => this.GetMachineType());
        }

        private AssemblyName GetAssemblyName()
        {
            return AssemblyName.GetAssemblyName(file.FullName);
        }

        private Version GetAssemblyVersion()
        {
            return assemblyName.Value.Version;
        }

        private string InitializeFramework()
        {
            ////Concord\src\StandaloneSetup\AssemblyReferenceChecker\ImportedAssembly.cs
            //using var stream = File.OpenRead(file.FullName);
            //using var reader = new PEReader(stream);

            //if (!reader.HasMetadata)
            //{
            //    return String.Empty;
            //}

            //var metadataReader = reader.GetMetadataReader();

            //// look for System.Runtime.Versioning.TargetFrameworkAttribute
            //var attributeHandle = metadataReader.CustomAttributes.Cast<CustomAttributeHandle>().SingleOrDefault(metadataReader.IsTargetFrameworkMonikerAttribute);
            //if (attributeHandle.IsNil)
            //    return String.Empty;
            //var parameters = metadataReader.GetParameterValues(metadataReader.GetCustomAttribute(attributeHandle));

            //return parameters.FirstOrDefault();
            return string.Empty;
        }

        private string GetPublicKey()
        {
            return HexByteArrayToString(assemblyName.Value.GetPublicKey());
        }

        private string GetPublicKeyToken()
        {
            return HexByteArrayToString(assemblyName.Value.GetPublicKeyToken());
        }

        private static string HexByteArrayToString(byte[] b)
        {
            if (b != null && b.Any())
            {
                return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            }
            return string.Empty;
        }

        public override int GetHashCode()
        {
            var hash = FName.Value.GetStableHashCode() ^ AssemblyVersion.Value.ToString(4).GetStableHashCode() ^ FileVersion.Value.ToString(4).GetStableHashCode() ^ FileSize.Value.ToString("0x{0:X}").GetStableHashCode();

            if (!string.IsNullOrWhiteSpace(PublicKeyToken.Value))
                return hash ^ PublicKeyToken.Value.GetStableHashCode();

            return hash;
        }
    }

    internal static class MetadataReaderExtensions
    {
        public static bool IsTargetFrameworkMonikerAttribute(this MetadataReader metadataReader, CustomAttributeHandle handle)
        {
            if (handle.IsNil)
            {
                return false;
            }
            var customAttribute = metadataReader.GetCustomAttribute(handle);
            if (customAttribute.Constructor.Kind != HandleKind.MemberReference)
            {
                return false;
            }
            if (customAttribute.Parent.Kind == HandleKind.TypeReference)
            {
                return false;
            }
            var constructorRef = metadataReader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
            if (constructorRef.Parent.Kind != HandleKind.TypeReference)
            {
                return false;
            }
            var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)constructorRef.Parent);
            var typeRefName = metadataReader.GetString(typeRef.Name);
            var typeRefNamespace = metadataReader.GetString(typeRef.Namespace);
            return string.Equals(typeRefName, "TargetFrameworkAttribute", StringComparison.Ordinal)
                && string.Equals(typeRefNamespace, "System.Runtime.Versioning", StringComparison.Ordinal);
        }

    //    public static ImmutableArray<string> GetParameterValues(this MetadataReader metadataReader, CustomAttribute customAttribute)
    //    {
    //        if (customAttribute.Constructor.Kind != HandleKind.MemberReference)
    //        {
    //            throw new InvalidOperationException();
    //        }
    //        var ctor = metadataReader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
    //        var provider = new StringParameterValueTypeProvider(metadataReader, customAttribute.Value);
    //        var signature = ctor.DecodeMethodSignature<string, object>(provider);
    //        return signature.ParameterTypes;
    //    }
    }

    internal static class StringExtension
    {
        public static int GetStableHashCode(this string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1 || str[i + 1] == '\0')
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }
    }
}