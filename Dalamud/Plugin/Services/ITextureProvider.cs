﻿using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Interface;
using Dalamud.Interface.Internal;

using Lumina.Data.Files;

namespace Dalamud.Plugin.Services;

/// <summary>Service that grants you access to textures you may render via ImGui.</summary>
/// <remarks>
/// <para>
/// <b>Get</b> functions will return a shared texture, and the returnd instance of <see cref="ISharedImmediateTexture"/>
/// do not require calling <see cref="IDisposable.Dispose"/>, unless a new reference has been created by calling
/// <see cref="ISharedImmediateTexture.RentAsync"/>.<br />
/// Use <see cref="ISharedImmediateTexture.TryGetWrap"/> and alike to obtain a reference of
/// <see cref="IDalamudTextureWrap"/> that will stay valid for the rest of the frame.
/// </para>
/// <para>
/// <b>Create</b> functions will return a new texture, and the returned instance of <see cref="IDalamudTextureWrap"/>
/// must be disposed after use.
/// </para>
/// </remarks>
public partial interface ITextureProvider
{
    /// <summary>Gets a texture from the given bytes, trying to interpret it as a .tex file or other well-known image
    /// files, such as .png.</summary>
    /// <param name="bytes">The bytes to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> containing the loaded texture on success. Dispose after use.</returns>
    Task<IDalamudTextureWrap> CreateFromImageAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a texture from the given stream, trying to interpret it as a .tex file or other well-known image
    /// files, such as .png.</summary>
    /// <param name="stream">The stream to load data from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open once the task completes, sucessfully or not.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> containing the loaded texture on success. Dispose after use.</returns>
    /// <remarks><paramref name="stream"/> will be closed or not only according to <paramref name="leaveOpen"/>;
    /// <paramref name="cancellationToken"/> is irrelevant in closing the stream.</remarks>
    Task<IDalamudTextureWrap> CreateFromImageAsync(
        Stream stream,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a texture from the given bytes, interpreting it as a raw bitmap.</summary>
    /// <param name="specs">The specifications for the raw bitmap.</param>
    /// <param name="bytes">The bytes to load.</param>
    /// <returns>The texture loaded from the supplied raw bitmap. Dispose after use.</returns>
    IDalamudTextureWrap CreateFromRaw(
        RawImageSpecification specs,
        ReadOnlySpan<byte> bytes);

    /// <summary>Gets a texture from the given bytes, interpreting it as a raw bitmap.</summary>
    /// <param name="specs">The specifications for the raw bitmap.</param>
    /// <param name="bytes">The bytes to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> containing the loaded texture on success. Dispose after use.</returns>
    Task<IDalamudTextureWrap> CreateFromRawAsync(
        RawImageSpecification specs,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a texture from the given stream, interpreting the read data as a raw bitmap.</summary>
    /// <param name="specs">The specifications for the raw bitmap.</param>
    /// <param name="stream">The stream to load data from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open once the task completes, sucessfully or not.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> containing the loaded texture on success. Dispose after use.</returns>
    /// <remarks><paramref name="stream"/> will be closed or not only according to <paramref name="leaveOpen"/>;
    /// <paramref name="cancellationToken"/> is irrelevant in closing the stream.</remarks>
    Task<IDalamudTextureWrap> CreateFromRawAsync(
        RawImageSpecification specs,
        Stream stream,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a texture handle for the specified Lumina <see cref="TexFile"/>.
    /// Alias for fetching <see cref="Task{TResult}.Result"/> from <see cref="CreateFromTexFileAsync"/>.
    /// </summary>
    /// <param name="file">The texture to obtain a handle to.</param>
    /// <returns>A texture wrap that can be used to render the texture. Dispose after use.</returns>
    IDalamudTextureWrap CreateFromTexFile(TexFile file);

    /// <summary>
    /// Get a texture handle for the specified Lumina <see cref="TexFile"/>.
    /// </summary>
    /// <param name="file">The texture to obtain a handle to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A texture wrap that can be used to render the texture. Dispose after use.</returns>
    Task<IDalamudTextureWrap> CreateFromTexFileAsync(
        TexFile file,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a shared texture corresponding to the given game resource icon specifier.</summary>
    /// <param name="lookup">A game icon specifier.</param>
    /// <returns>The shared texture that you may use to obtain the loaded texture wrap and load states.</returns>
    ISharedImmediateTexture GetFromGameIcon(in GameIconLookup lookup);

    /// <summary>Gets a shared texture corresponding to the given path to a game resource.</summary>
    /// <param name="path">A path to a game resource.</param>
    /// <returns>The shared texture that you may use to obtain the loaded texture wrap and load states.</returns>
    ISharedImmediateTexture GetFromGame(string path);

    /// <summary>Gets a shared texture corresponding to the given file on the filesystem.</summary>
    /// <param name="path">A path to a file on the filesystem.</param>
    /// <returns>The shared texture that you may use to obtain the loaded texture wrap and load states.</returns>
    ISharedImmediateTexture GetFromFile(string path);

    /// <summary>Gets a shared texture corresponding to the given file of the assembly manifest resources.</summary>
    /// <param name="assembly">The assembly containing manifest resources.</param>
    /// <param name="name">The case-sensitive name of the manifest resource being requested.</param>
    /// <returns>The shared texture that you may use to obtain the loaded texture wrap and load states.</returns>
    ISharedImmediateTexture GetFromManifestResource(Assembly assembly, string name);

    /// <summary>
    /// Get a path for a specific icon's .tex file.
    /// </summary>
    /// <param name="lookup">The icon lookup.</param>
    /// <returns>The path to the icon.</returns>
    /// <exception cref="FileNotFoundException">If a corresponding file could not be found.</exception>
    string GetIconPath(in GameIconLookup lookup);

    /// <summary>
    /// Gets the path of an icon.
    /// </summary>
    /// <param name="lookup">The icon lookup.</param>
    /// <param name="path">The resolved path.</param>
    /// <returns><c>true</c> if the corresponding file exists and <paramref name="path"/> has been set.</returns>
    bool TryGetIconPath(in GameIconLookup lookup, [NotNullWhen(true)] out string? path);

    /// <summary>
    /// Determines whether the system supports the given DXGI format.
    /// For use with <see cref="RawImageSpecification.DxgiFormat"/>.
    /// </summary>
    /// <param name="dxgiFormat">The DXGI format.</param>
    /// <returns><c>true</c> if supported.</returns>
    bool IsDxgiFormatSupported(int dxgiFormat);
}
