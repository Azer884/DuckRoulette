Hello,

Gif2Textures is a Unity plugin that allows you to use animated Gif as texture.

First of all, please check the demo http://www.cheng.byethost18.com/Gif2Textures/Gif2Textures.html to get an idea of how it looks like.

It is very easy to use.
------------------
If you are an artist or a designer (I mean if you don't know anything about scripting):
------------------
1. Put Gif file to StreamingAssets folder. Or put Gif file to Resources folder and change the file extension to "bytes". If you will play a remote Gif by url, skip this step.
2. Add component "Gif Texture" to your game object. Your game object has to have a "Mesh Renderer" component with a valid texture material, or a "RawImage" component, or a "Image" component (use RawImage if you can because it has better performance than Image). Otherwise, component "Gif Texture" won't work because there is no way to set texture.
3. On inspector of component "Gif Texture", fill the name or url of your Gif to the property "Gif File Name Or Url".
4. You can tweak below parameters to balance the memory use and performance:
	"Shrink Frame To Fit Texture"
		Tick this if you want to save some memory. Then set "Max Texture Size" accordingly.
		
	"Enable Atlas"
		Strongly suggest you to enable atlas. Using atlas will improve both memory use and performance (FPS).
		
	"Max Texture Size"
		If "Enable Atlas" is enabled, this parameter means the size of the atlas texture. In this case, you should just set it to the maximum texture size of your target platform because then memory will be used more efficiently.
		If "Enable Atlas" is disabled and "Shrink Frame To Fit Texture" is enabled, this parameter means the size each frame will shrink to if it is smaller than the Gif frame size. 
		If both "Enable Atlas" and "Shrink Frame To Fit Texture" are disabled, this parameter will be ignored.
		
	"Exclude First Frame From Atlas"
		Tick this if you want to use the 1st frame of the Gif to replace the loading texture asap. Please be aware that usually it will take a little bit more memory.
		
	"Max Atlas Texture Count"
		Only works if both "Enable Atlas" and "Shrink Frame To Fit Texture" are enabled. If "Exclude First Frame From Atlas" is enabled, the total texture count will be "Max Atlas Texture Count" + 1.
		
	"Enable Mipmap"
		Tick it if you will use the textures on 3D object. Mipmap will use one-third more memory but it gives better performance.
		
	"Enable Async Loading"
		Loads Gif asynchronously, so user interaction or UI won't be blocked. Please be aware that it will automatically fall back to synchronous loading on platforms that don't support multi-threading (e.g. WebGL).
		
	"Playback Speed"
		For example, 1.0: original speed; 0.1: one tenth of the original speed; 10.0: 10 times of the original speed.
		
	"Enable Texture Caching"
		Caching generated textures will make loading faster next time. Only works with remote Gif. For local Gif, try use the plug-in "Gif2Sprite" to pre-process it.
		
	"Material Index"
		Choose which material to update when there are multiple materials in the renderer.
		
	"Loading Texture"
		The texture that will be shown before the Gif is loaded.

------------------
If you know scripting, you can use Gif2Textures.dll directly:
------------------
1. Import namespace Gif2Texture in your script.
2. New a GifFrames instance.
3. Pass the stream of Gif and your loading configuration to the GifFrames by function GifFrames.Load() or GifFrames.LoadAsync().
4. Then you can get textures of every frame and their delay time by calling function GifFrames.GetFrameTexture() and GifFrames.GetFrameDelay().

You can check GifTexture.cs as an example for how to use Gif2Textures.dll directly.

I look forward to hear your questions or suggestions. Please email to fengcheng0308@gmail.com.

And I'd appreciate it if you could leave your user review to Asset Store.

Thanks in advance!

Cheng