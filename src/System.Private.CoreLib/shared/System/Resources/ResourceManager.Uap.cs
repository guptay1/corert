// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Globalization;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Internal.Resources;

namespace System.Resources
{
    public partial class ResourceManager
    {
        private WindowsRuntimeResourceManagerBase? _WinRTResourceManager;
        private PRIExceptionInfo? _PRIExceptionInfo;
        private bool _PRIInitialized;
        private bool _useUapResourceManagement;

        private string? GetStringFromPRI(string stringName, CultureInfo? culture, string? neutralResourcesCulture)
        {
            Debug.Assert(_useUapResourceManagement);
            Debug.Assert(_WinRTResourceManager != null);
            Debug.Assert(_PRIInitialized);

            // If the caller explicitly passed in a culture that was obtained by calling CultureInfo.CurrentUICulture,
            // null it out, so that we re-compute it.  If we use modern resource lookup, we may end up getting a "better"
            // match, since CultureInfo objects can't represent all the different languages the Uap resource model supports.
            if (object.ReferenceEquals(culture, CultureInfo.CurrentUICulture))
            {
                culture = null;
            }

            string? startingCulture = culture?.Name;

            if (!_PRIInitialized)
            {
                // Always throw if we did not fully succeed in initializing the WinRT Resource Manager.

                if (_PRIExceptionInfo != null && _PRIExceptionInfo.PackageSimpleName != null && _PRIExceptionInfo.ResWFile != null)
                    throw new MissingManifestResourceException(SR.Format(SR.MissingManifestResource_ResWFileNotLoaded, _PRIExceptionInfo.ResWFile, _PRIExceptionInfo.PackageSimpleName));

                throw new MissingManifestResourceException(SR.MissingManifestResource_NoPRIresources);
            }

            if (stringName.Length == 0)
                return null;

            // Do not handle exceptions. See the comment in SetUapConfiguration about throwing
            // exception types that the ResourceManager class is not documented to throw.
            return _WinRTResourceManager.GetString(
                                       stringName,
                                       string.IsNullOrEmpty(startingCulture) ? null : startingCulture,
                                       string.IsNullOrEmpty(neutralResourcesCulture) ? null : neutralResourcesCulture);
        }

        // Since we can't directly reference System.Runtime.WindowsRuntime from System.Private.CoreLib, we have to get the type via reflection.
        // It would be better if we could just implement WindowsRuntimeResourceManager in System.Private.CoreLib, but we can't, because
        // we can do very little with WinRT in System.Private.CoreLib.
        // The attribute is necessary because linker can't add new assemblies to the closure when recognizing Type.GetType
        // so even though the GetType call below is analyzable, the PreserveDependency is still necessary to actually include
        // the assembly in the trimmed closure.
        [PreserveDependency(".ctor()", "System.Resources.WindowsRuntimeResourceManager", "System.Runtime.WindowsRuntime")]
        internal static WindowsRuntimeResourceManagerBase GetWinRTResourceManager()
        {
            Type WinRTResourceManagerType = Type.GetType("System.Resources.WindowsRuntimeResourceManager, System.Runtime.WindowsRuntime", throwOnError: true)!;
            return (WindowsRuntimeResourceManagerBase)Activator.CreateInstance(WinRTResourceManagerType, nonPublic: true)!;
        }

        // CoreCLR: When running under AppX, the following rules apply for resource lookup:
        //
        // 1) For Framework assemblies, we always use satellite assembly based lookup.
        // 2) For non-FX assemblies:
        //
        //    a) If the assembly lives under PLATFORM_RESOURCE_ROOTS (as specified by the host during AppDomain creation),
        //       then we will use satellite assembly based lookup in assemblies like *.resources.dll.
        //
        //    b) For any other non-FX assembly, we will use the modern resource manager with the premise that app package
        //       contains the PRI resources.
        //
        // .NET Native: If it is framework assembly we'll return true. The reason is in .NetNative we don't merge the
        // resources to the app PRI file.
        // The framework assemblies are tagged with attribute [assembly: AssemblyMetadata(".NETFrameworkAssembly", "")]
        private static bool ShouldUseUapResourceManagement(Assembly resourcesAssembly)
        {
            if (resourcesAssembly == typeof(object).Assembly) // We are not loading resources for System.Private.CoreLib
                return false;

            // Check to see if the assembly is under PLATFORM_RESOURCE_ROOTS. If it is, then we should use satellite assembly lookup for it.
            string? platformResourceRoots = AppContext.GetData("PLATFORM_RESOURCE_ROOTS") as string;
            if (!string.IsNullOrEmpty(platformResourceRoots))
            {
                string resourceAssemblyPath = resourcesAssembly.Location;

                // Loop through the PLATFORM_RESOURCE_ROOTS and see if the assembly is contained in it.
                foreach (string pathPlatformResourceRoot in platformResourceRoots.Split(Path.PathSeparator))
                {
                    if (resourceAssemblyPath.StartsWith(pathPlatformResourceRoot, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Found the resource assembly to be present in one of the PLATFORM_RESOURCE_ROOT, so stop the enumeration loop.
                        return false;
                    }
                }
            }

            return true;
        }

        // Only call SetUapConfiguration from ResourceManager constructors, and nowhere else.
        // Throws MissingManifestResourceException and WinRT HResults
        private void SetUapConfiguration()
        {
            Debug.Assert(!_useUapResourceManagement); // Only this function writes to this member
            Debug.Assert(_WinRTResourceManager == null); // Only this function writes to this member
            Debug.Assert(!_PRIInitialized); // Only this function writes to this member
            Debug.Assert(_PRIExceptionInfo == null); // Only this function writes to this member

            if (!ApplicationModel.IsUap)
                return;

            Debug.Assert(MainAssembly != null);
            if (!ShouldUseUapResourceManagement(MainAssembly))
                return;

            _useUapResourceManagement = true;

            // If we have the type information from the ResourceManager(Type) constructor, we use it. Otherwise, we use BaseNameField.
            string? reswFilename = _locationInfo == null ? BaseNameField : _locationInfo.FullName;

            // The only way this can happen is if a class inherited from ResourceManager and
            // did not set the BaseNameField before calling the protected ResourceManager() constructor.
            // For other constructors, we would already have thrown an ArgumentNullException by now.
            // Throwing an ArgumentNullException now is not the right thing to do because technically
            // ResourceManager() takes no arguments, and because it is not documented as throwing
            // any exceptions. Instead, let's go through the rest of the initialization with this set to
            // an empty string. We may in fact fail earlier for another reason, but otherwise we will
            // throw a MissingManifestResourceException when GetString is called indicating that a
            // resW filename called "" could not be found.
            reswFilename ??= string.Empty;

            // At this point it is important NOT to set _useUapResourceManagement to false
            // if the PRI file does not exist because we are now certain we need to load PRI
            // resources. We want to fail by throwing a MissingManifestResourceException
            // if WindowsRuntimeResourceManager.Initialize fails to locate the PRI file. We do not
            // want to fall back to using satellite assemblies anymore. Note that we would not throw
            // the MissingManifestResourceException from this function, but from GetString. See the
            // comment below on the reason for this.

            _WinRTResourceManager = GetWinRTResourceManager();

            try
            {
                _PRIInitialized = _WinRTResourceManager.Initialize(MainAssembly.Location, reswFilename, out _PRIExceptionInfo);
                // Note that _PRIExceptionInfo might be null - this is OK.
                // In that case we will just throw the generic
                // MissingManifestResource_NoPRIresources exception.
                // See the implementation of GetString for more details.
            }
            // We would like to be able to throw a MissingManifestResourceException here if PRI resources
            // could not be loaded for a recognized reason. However, the ResourceManager constructors
            // that call SetUapConfiguration are not documented as throwing MissingManifestResourceException,
            // and since they are part of the portable profile, we cannot start throwing a new exception type
            // as that would break existing portable libraries. Hence we must save the exception information
            // now and throw the exception on the first call to GetString.
            catch (FileNotFoundException)
            {
                // We will throw MissingManifestResource_NoPRIresources from GetString
                // when we see that _PRIInitialized is false.
            }
            catch (Exception e)
            {
                // ERROR_MRM_MAP_NOT_FOUND can be thrown by the call to ResourceManager.get_AllResourceMaps
                // in WindowsRuntimeResourceManager.Initialize.
                // In this case _PRIExceptionInfo is now null and we will just throw the generic
                // MissingManifestResource_NoPRIresources exception.
                // See the implementation of GetString for more details.
                if (e.HResult != HResults.ERROR_MRM_MAP_NOT_FOUND)
                    throw; // Unexpected exception code. Bubble it up to the caller.
            }

            if (!_PRIInitialized)
            {
                _useUapResourceManagement = false;
            }

            // Allow all other exception types to bubble up to the caller.

            // Yes, this causes us to potentially throw exception types that are not documented.

            // Ultimately the tradeoff is the following:
            // -We could ignore unknown exceptions or rethrow them as inner exceptions
            // of exceptions that the ResourceManager class is already documented as throwing.
            // This would allow existing portable libraries to gracefully recover if they don't care
            // too much about the ResourceManager object they are using. However it could
            // mask potentially fatal errors that we are not aware of, such as a disk drive failing.

            // The alternative, which we chose, is to throw unknown exceptions. This may tear
            // down the process if the portable library and app don't expect this exception type.
            // On the other hand, this won't mask potentially fatal errors we don't know about.
        }
    }
}
