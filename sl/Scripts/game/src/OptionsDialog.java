 package java.game;

import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.render.*;	//Text
import java.render.osd.*;	//Text
import java.render.osd.dialog.*;	//Text
import java.sound.*;

public class OptionsDialog extends Dialog
{
	final static ResourceRef	RID_GENERALBG = new ResourceRef( frontend:0x0016r );
	
	// commands
	final static int	CMD_OPTIONS = 0;
	final static int	CMD_VIDEO_OPTIONS = 1;
	final static int	CMD_CONTROL_OPTIONS = 2;
	final static int	CMD_SOUND_OPTIONS = 3;
	final static int	CMD_REDEFINE_CONTROLS = 4;
	final static int	CMD_MAIN = 5;
	final static int	CMD_REDEFINE_DONE = 6;
	final static int	CMD_RESET_CONTROLS = 7;
	final static int	CMD_LOAD_CONTROLS = 8;
	final static int	CMD_SAVE_CONTROLS = 9;
	final static int	CMD_RESOLUTION = 10;
	final static int	CMD_VIDEO_OPTIONS_DONE = 11;
	final static int	CMD_TEXTURE_DETAIL = 12;
	final static int	CMD_SHADOW_DETAIL = 13;
	final static int	CMD_VIEW_RANGE = 14;
	final static int	CMD_EFFECTS_VOL = 15;
	final static int	CMD_MUSIC_VOL = 16;
	final static int	CMD_ENGINE_VOL = 17;
	final static int	CMD_GAME_OPTIONS = 18;
	final static int	CMD_DIFFICULTY = 19;
	final static int	CMD_TRAFFICDENSITY = 20;
	final static int	CMD_PEDESTRIANDENSITY = 21;
	final static int	CMD_CLUTCH = 22;
	final static int	CMD_STEERHELP = 23;
	final static int	CMD_GAME_OPTIONS_DONE = 24;
	final static int	CMD_MOUSE_SENS = 25;

	final static int	CMD_REDEFINE_CONTROLS2 = 26;
	final static int	CMD_REDEFINE_CONTROLS3 = 27;
	final static int	CMD_METRIC = 28;
	final static int	CMD_GAMMA = 29;
	final static int	CMD_OBJECT_DETAIL = 30;
	final static int	CMD_LOD_DETAIL = 31;
	final static int	CMD_ABS_SLIDER = 32;
	final static int	CMD_ASR_SLIDER = 33;
	final static int	CMD_GPSMODE = 34;
	final static int	CMD_AXISCHECK = 35;
	final static int	CMD_AXISCHECK_DONE = 36;
	final static int	CMD_UNUSED = 37;
	final static int	CMD_SOUND_3D = 38;
	final static int	CMD_SOUND_HW = 39;
	final static int	CMD_GAME_OPTIONS2 = 40;
	final static int	CMD_HMF_1 = 41;
	final static int	CMD_HMF_2 = 42;
	final static int	CMD_HMF_3 = 43;
	final static int	CMD_PARTICLE = 44;
	final static int	CMD_HEADLIGHTS = 45;
	final static int	CMD_FLARES = 46;
	
	final static int	CMD_SKIDMARK = 402;
	final static int	CMD_SHADOW_RANGE = 403;
	final static int	CMD_REFRESH_TEXT = 404;
	final static int	CMD_MIRROR_DISTANCE = 405;
	final static int	CMD_DEPTH = 406;
	final static int	CMD_MODE = 407;
	final static int	CMD_UNRES = 408;
	final static int	CMD_MIRROR = 409;
	final static int	CMD_BGSCENE = 410;
	final static int	CMD_M_GROUP = 411;
	final static int	CMD_LP = 412;
	final static int	CMD_RED_SUSP = 413;

	final static int	CMD_GETKEY = 100;
	final static int	CMD_DEAD_ZONE = 200;
	final static int	CMD_POWER = 300;
	final static int	CMD_FFB = 400;
	final static int	CMD_COMPILE= 401;

	final static int	NCONTROLS = ControlSet.NCONTROLS;

	static String[] soundHWText;
	static String[] TextureSizeText;
	static String[] ShadowSizeText;
	static String[] LP_TexList;
	static String[] detailTextOO;
	static String[] metricSystemText;
	static String[] gpsText = new String[4];
	static int[]	ResX_list, ResY_list;
	
	final static float	aspectX, aspectY;
	String	Res_Aspect;
	int		Current_Resolution, aspect, color_depth, windowed, loading_primary, tmp_lp;

	Button			Resolution_button, MirrorDist_Text, Dist_Text, ShadowDist_Text, Particle_Text, SkidMark_Text, Depth_button, windowed_button;

	Player			player;

	int				getKey = -1;
	int				getName = -1;
	int				getKeyStat = 0;

	Text[]			keyText = new Text[NCONTROLS];

	String[]		clutchText = new String[4];
	int[]			clutchTrf, clutchTrfInv;

	Text			accelerateText,	brakeText, steeringText;

	int				axisCheckCallerGroup;

	int				mainGroup, optionsGroup, videoGroup, soundGroup, controlGroup, gameGroup, game2Group, MirrorGroup;
	int				redefineSuspGroup, redefineGroup, redefine2Group, redefine3Group, axisCheckGroup;

	int				actGroup;

	int				textureDetail;
	int				shadow_size;
	int				shadow_distance;

	MultiChoice		soundHWMulti;

	Slider[]		powerSlider;
	Slider[]		dzSlider;
	Slider[]		ffbSlider;

	Slider			acGasSlider, acBreakSlider, acSteerSlider, acClutchSlider, TextureSlider;

	
	ControlSet		controlSet;

	Style butt0 = new Style( 0.45, 0.12, Frontend.mediumFont, Text.ALIGN_LEFT, Osd.RRT_TEST );
	Style butt1 = new Style( 0.45, 0.12, Frontend.mediumFont, Text.ALIGN_CENTER, Osd.RRT_TEST );
	Style butt2 = new Style( 0.45, 0.12, Frontend.mediumFont, Text.ALIGN_RIGHT, Osd.RRT_TEST );

	Style buttX = new Style( 0.1, 0.1, Frontend.mediumFont, Text.ALIGN_RIGHT, Osd.RRT_TEST );

	Style sld1 = new Style( 0.45, 0.06, Frontend.mediumFont, Text.ALIGN_LEFT, new ResourceRef(Osd.RID_SLD_BACK) );
	Style sldk =  new Style( 0.04, 0.05, Frontend.mediumFont, Text.ALIGN_RIGHT, new ResourceRef(Osd.RID_SLD_KNOB) );
	Style sld2 = new Style( 0.8, 0.06, Frontend.mediumFont, Text.ALIGN_CENTER, new ResourceRef(Osd.RID_SLD_BACK) );

	Style Menu_Left = new Style( 0.6, 0.1, Frontend.mediumFont, Text.ALIGN_LEFT, Osd.RRT_TEST );
	Style Menu_Right = new Style( 0.6, 0.1, Frontend.mediumFont, Text.ALIGN_RIGHT, Osd.RRT_TEST );
	Style Menu_Center = new Style( 0.6, 0.1, Frontend.mediumFont, Text.ALIGN_CENTER, Osd.RRT_TEST );

	Style Menu_Left_Small = new Style( 0.6, 0.1, Frontend.smallFont, Text.ALIGN_LEFT, Osd.RRT_TEST );
	Style Menu_Right_Small = new Style( 0.6, 0.1, Frontend.smallFont, Text.ALIGN_RIGHT, Osd.RRT_TEST );
	Style Menu_Center_Small = new Style( 0.6, 0.1, Frontend.smallFont, Text.ALIGN_CENTER, Osd.RRT_TEST );

	public OptionsDialog( int additionalFlags )
	{
		super( GameLogic.player.controller, additionalFlags|DF_FULLSCREEN|DF_LOWPRI, null, null );

		float key = Math.random()*100;
		if( MainMenu.Check(key) != Math.sqrt(key)+3.75 ) Mirans_Mod_protection_error______Please_Resintal_Mirans_Mod();

		player=GameLogic.player;

		osd.globalHandler = this;

		actGroup = -1;

		ResX_list = new int[20];
		ResX_list[0] = 160;
		ResX_list[1] = 213;
		ResX_list[2] = 320;
		ResX_list[3] = 320;
		ResX_list[4] = 400;
		ResX_list[5] = 480;
		ResX_list[6] = 512;
		ResX_list[7] = 640;
		ResX_list[8] = 640;
		ResX_list[9] = 720;
		ResX_list[10] = 720;
		ResX_list[11] = 800;
		ResX_list[12] = 848;
		ResX_list[13] = 960;
		ResX_list[14] = 1024;
		ResX_list[15] = 1152;
		ResX_list[16] = 1280;
		ResX_list[17] = 1280;
		ResX_list[18] = 1280;
		ResX_list[19] = 1280;

		ResY_list = new int[20];
		ResY_list[0] = 120;
		ResY_list[1] = 160;
		ResY_list[2] = 200;
		ResY_list[3] = 240;
		ResY_list[4] = 300;
		ResY_list[5] = 360;
		ResY_list[6] = 384;
		ResY_list[7] = 400;
		ResY_list[8] = 480;
		ResY_list[9] = 480;
		ResY_list[10] = 576;
		ResY_list[11] = 600;
		ResY_list[12] = 480;
		ResY_list[13] = 720;
		ResY_list[14] = 768;
		ResY_list[15] = 864;
		ResY_list[16] = 720;
		ResY_list[17] = 768;
		ResY_list[18] = 960;
		ResY_list[19] = 1024;

		detailTextOO = new String[2];
		detailTextOO[0] = "OFF";		
		detailTextOO[1] = "ON";	
		
		LP_TexList = new String[3];
		LP_TexList[0] = "LOW (Best)";		
		LP_TexList[1] = "MEDIUM";
		LP_TexList[2] = "DEFAULT";
		
		TextureSizeText = new String[7];
		TextureSizeText[0] = "64 PTX";
		TextureSizeText[1] = "124 PTX";
		TextureSizeText[2] = "256 PTX";
		TextureSizeText[3] = "512 PTX";
		TextureSizeText[4] = "1024 PTX";
		TextureSizeText[5] = "2048 PTX";
		TextureSizeText[6] = "4096 PTX";

		ShadowSizeText = new String[8];
		ShadowSizeText[0] = "OFF";
		ShadowSizeText[1] = "124 PTX";
		ShadowSizeText[2] = "256 PTX";
		ShadowSizeText[3] = "512 PTX";
		ShadowSizeText[4] = "1024 PTX";
		ShadowSizeText[5] = "2048 PTX";	
		ShadowSizeText[6] = "4096 PTX";
		ShadowSizeText[7] = "8192 PTX";

		//
		soundHWText = new String[4];
		soundHWText[0] = "SOFTWARE (SLOW!)";
		soundHWText[1] = "HARDWARE (2D ONLY)";
		soundHWText[2] = "HARDWARE (FULL 3D)";
		soundHWText[3] = "AUTO";

		//
		clutchText[0] = "Auto+Autoclutch";
		clutchText[1] = "Semiauto+Autoclutch";
		clutchText[2] = "Manual+Autoclutch";
		clutchText[3] = "Manual+Clutch";

		clutchTrf = new int[6];	//player_transmission values -> clutch mode name indexes
		clutchTrf[0] = 2;
		clutchTrf[1] = 0;
		clutchTrf[2] = 3;
		clutchTrf[3] = -1;
		clutchTrf[4] = -1;
		clutchTrf[5] = 1;

		clutchTrfInv = new int[4];	//clutch mode name indexes -> player_transmission values
		clutchTrfInv[0] = 1;
		clutchTrfInv[1] = 5;
		clutchTrfInv[2] = 0;
		clutchTrfInv[3] = 2;

		metricSystemText = new String[2];
		metricSystemText[0]="Miles";
		metricSystemText[1]="Kilometres";

		gpsText[0] = "FOLLOW CAR, MAP RELATIVE";
		gpsText[1] = "FOLLOW CAR, CAR RELATIVE";
		gpsText[2] = "FOLLOW CAMERA, MAP RELATIVE";
		gpsText[3] = "FOLLOW CAMERA, CAMERA RELATIVE";
	}

	public void show()
	{
	// protection
		float key = Math.random()*100;
		if( MainMenu.Check(key) != Math.sqrt(key)+3.75 )
		{
			new WarningDialog( GameLogic.player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "PROTECTION ERROR", "Miran Wichur Mod protection error: \n \n CORRUPTED FILES \n \n Sorry game will be close. \n Please reinstall Miran Wichur Mod." ).display();
			GameLogic.changeActiveSection( null );
		}
	
	// find the settings
		aspectX = Config.video_x;
		aspectY = Config.video_y;
		color_depth = Config.video_depth;
		windowed = Config.video_windowed;

	// Look for current resolution mode
		for (int i=0; i<=ResX_list.length-1; i++ ) 
		{
			if( aspectX == ResX_list[i] ) // identical only X size (used UNCOMMON RES, or similar mode)
			{
				Current_Resolution = i; 

				if(aspectX == ResX_list[i]) // found identical mode
				{
					Current_Resolution = i;
					i += ResX_list.length; //end 'for' loop
				}
			}
		}

	// Set current texture size
		textureDetail = 6-Config.texture_size; // if 6=>0, if 2=>3 ...

	// Set loading pimary

		if( Config.resource_refreshrate == 2 )
			loading_primary = 0;
		else
		if( Config.resource_refreshrate == 1024 )
			loading_primary = 1;
		else
		if( Config.resource_refreshrate == 4096 )
			loading_primary = 2;

		tmp_lp = loading_primary;

	// Set current shadow size
		if( Config.shadow_size == 8192 )
			shadow_size = 7;
		else 
		if( Config.shadow_size == 4096 )
			shadow_size = 6;
		else 
		if( Config.shadow_size == 2048 )
			shadow_size = 5;
		else 
		if( Config.shadow_size == 1024 )
			shadow_size = 4;
		else 
		if( Config.shadow_size == 512 )
			shadow_size = 3;
		else 
		if( Config.shadow_size == 256 )
			shadow_size = 2;
		else 
		if( Config.shadow_size == 128 )
			shadow_size = 1;
		else 
		if( Config.shadow_size < 64 )
			shadow_size = 0;

		float x, y, spc;
		float lpos = -1.0, mpos = -0.10;

		//--------------main menu--------------
		addCustomGroups();

		//--------------options menu--------------
		Menu m;

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "options" );

		m = osd.createMenu( butt1, 0.0, -0.6, 0 );
		m.addItem( "video options", CMD_VIDEO_OPTIONS );
		m.addItem( "sound options", CMD_SOUND_OPTIONS );
		m.addItem( "control options", CMD_CONTROL_OPTIONS );
		m.addItem( "game options", CMD_GAME_OPTIONS );
		m.addItem( "advanced game options", CMD_GAME_OPTIONS2 );
		
		m = osd.createMenu( butt0, -0.98, 0.85, 0 );
		m.addItem( "DEBUG COMPILE ALL JAVAs", CMD_COMPILE );

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_MAIN );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN, this );
		osd.hideGroup( optionsGroup = osd.endGroup() );

		//-----------video options menu--------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "video optons" );

	// Left side:
		m = osd.createMenu( Menu_Left, -0.95, -0.6, 0 );
		Resolution_button = m.addItem( "screen resolution: --- X --- (---)", CMD_RESOLUTION, "Get next standard resolution" );
		
		m = osd.createMenu( Menu_Center, -0.5, -0.5, 0 );
		Depth_button = m.addItem( "color depth: ---", CMD_DEPTH, null );
		
		m = osd.createMenu( Menu_Left, -0.4, -0.4, 0 );
		m.addItem( "texture detail", CMD_TEXTURE_DETAIL, textureDetail, TextureSizeText, null );

		m = osd.createMenu( Menu_Center_Small, -0.5, -0.2, 0 );
		Dist_Text = m.addItem( "view distance: " + Float.toString(Config.camera_ext_viewrange, "%.0f m"), CMD_UNUSED, null );
		m = osd.createMenu( Menu_Left_Small, -0.5, -0.15, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_VIEW_RANGE, Config.camera_ext_viewrange, 50, 2050, 81, null );

		m = osd.createMenu( Menu_Center_Small, -0.5, -0.05, 0 );
		m.addItem( "object detail", CMD_UNUSED, "How much details is visable" );
		m = osd.createMenu( Menu_Left_Small, -0.5, 0.0, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_OBJECT_DETAIL, Config.object_detail, 0.074, 0.01, 11, "How much details is visable" );//.printValue( "%2f" );

		m = osd.createMenu( Menu_Center_Small, -0.5, 0.10, 0 );
		m.addItem( "lod detail", CMD_UNUSED, "Distance of objects simplification" );
		m = osd.createMenu( Menu_Left_Small, -0.5, 0.15, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_LOD_DETAIL, Config.object_detail_amp, 35.0, 4.0, 11, "Distance of objects simplification" );//.printValue( "%.1f" );

		m = osd.createMenu( Menu_Left, -0.4, 0.28, 0 );
		m.addItem( "loading primary:", CMD_LP, loading_primary, LP_TexList, "Use LOW to fast game working" );

		m = osd.createMenu( Menu_Left, -0.5, 0.5, 0 );
		m.addItem( "flares:", CMD_FLARES, Config.flares, detailTextOO, null );
//		m.addItem( "mirror options", CMD_M_GROUP, "Settings of mirror" );

	// Right side:
		m = osd.createMenu( Menu_Center, 0.5, -0.6, 0 );
		m.addItem( "uncommon resolution", CMD_UNRES, "Set uncommon resolution" );
		windowed_button = m.addItem( "DISPLAY MODE: ---", CMD_MODE, null );

		m = osd.createMenu( Menu_Left, 0.3, -0.4, 0 );
		m.setSliderStyle( sld1, sldk );
		m.addItem( "gamma", CMD_GAMMA, Config.video_gamma, 0.4, 4.0, 0, "Max shadow texture size" ).printValue( "%.2f" );

		m = osd.createMenu( Menu_Left, 0.6, -0.18, 0 );
		m.addItem( "shadow detail", CMD_SHADOW_DETAIL, shadow_size, ShadowSizeText, "Max shadow texture size" );

		m = osd.createMenu( Menu_Center_Small, 0.5, -0.05, 0 );
		ShadowDist_Text = m.addItem( "shadow view distance: " + Float.toString(1/Config.shadow_detail*10, "%.0f m"), CMD_UNUSED, "Max shadow view distance" );
		m = osd.createMenu( Menu_Left_Small, 0.5, 0.0, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_SHADOW_RANGE, 1/Config.shadow_detail*10, 10, 210, 21, "Max shadow view distance" );

		m = osd.createMenu( Menu_Center_Small, 0.5, 0.10, 0 );
		Particle_Text = m.addItem( "particle density: " + Float.toString(Config.particle_density*100, "%.0f")+"%", CMD_UNUSED, "Density of tyres smoke, sparks etc." );
		m = osd.createMenu( Menu_Left_Small, 0.5, 0.15, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_PARTICLE, Config.particle_density, 0.0, 2.0, 9, "Density of tyres smoke, sparks etc." );//.printValue( "%.2f" );

		m = osd.createMenu( Menu_Center_Small, 0.5, 0.25, 0 );
		SkidMark_Text = m.addItem( "skidmarks length: "+Config.skidmark_max/4+" m", CMD_UNUSED, "Max length of skidmarks" );
		m = osd.createMenu( Menu_Left_Small, 0.5, 0.30, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_SKIDMARK, Config.skidmark_max/4, 50, 2050, 21, "Max length of skidmarks" );//.printValue( "%.0f m" );

		m = osd.createMenu( Menu_Left, 0.65, 0.5, 0 );
		m.addItem( "headlight rays:", CMD_HEADLIGHTS, Config.headlight_rays, detailTextOO, null );
//		m.addItem( "menu 3D scene:", CMD_BGSCENE, Config.BgScene, detailTextOO, null );

		
		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_VIDEO_OPTIONS_DONE );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_VIDEO_OPTIONS_DONE, this );
		osd.hideGroup( videoGroup = osd.endGroup() );

	// Mirror setings		
		//osd.createBG( RID_GENERALBG );
		osd.createHeader( "mirror options" );

		m = osd.createMenu( Menu_Left, -0.5, 0.5, 0 );
		m.addItem( "mirror:", CMD_MIRROR, Config.mirror, detailTextOO, null );

		m = osd.createMenu( Menu_Center_Small, -0.5, -0.05, 0 );
		MirrorDist_Text = m.addItem( "view distance: " + Float.toString(Config.camera_mirror_viewrange, "%.0f m"), CMD_UNUSED, null );
		m = osd.createMenu( Menu_Left_Small, -0.5, 0.0, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_MIRROR_DISTANCE, Config.camera_mirror_viewrange, 5, 105, 41, null );

		m = osd.createMenu( Menu_Center_Small, -0.5, 0.10, 0 );
		m.addItem( "object detail", CMD_UNUSED, "How much details is visable" );
		m = osd.createMenu( Menu_Left_Small, -0.5, 0.15, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_OBJECT_DETAIL, Config.object_detail, 0.074, 0.01, 11, "How much details is visable" );//.printValue( "%2f" );

		m = osd.createMenu( Menu_Center_Small, -0.5, 0.25, 0 );
		m.addItem( "lod detail", CMD_UNUSED, "Distance of objects simplification" );
		m = osd.createMenu( Menu_Left_Small, -0.5, 0.30, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_LOD_DETAIL, Config.object_detail_amp, 35.0, 4.0, 11, "Distance of objects simplification" );//.printValue( "%.1f" );

		// right side
		m = osd.createMenu( Menu_Left, 0.6, -0.04, 0 );
		m.addItem( "shadow detail", CMD_SHADOW_DETAIL, shadow_size, ShadowSizeText, "Max shadow texture size" );

		m = osd.createMenu( Menu_Center_Small, 0.5, 0.10, 0 );
		m.addItem( "shadow view distance: " + Float.toString(1/Config.shadow_detail*10, "%.0f m"), CMD_UNUSED, "Max shadow view distance" );
		m = osd.createMenu( Menu_Left_Small, 0.5, 0.15, 0 );
		m.setSliderStyle( sld2, sldk );
		m.addItem( null, CMD_SHADOW_RANGE, 1/Config.shadow_detail*10, 10, 210, 21, "Max shadow view distance" );

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_VIDEO_OPTIONS );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_VIDEO_OPTIONS, this );
		osd.hideGroup( MirrorGroup = osd.endGroup() );


		//-----------sound options menu--------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "sound options" );

		m = osd.createMenu( butt0, 0.0, -0.6, 0 );
		m.setSliderStyle( sld1, sldk );

		m.addItem( "effects volume", CMD_EFFECTS_VOL, Sound.getVolume(Sound.CHANNEL_EFFECTS), null );
		m.addItem( "music volume", CMD_MUSIC_VOL, Sound.getVolume(Sound.CHANNEL_MUSIC), null );
		m.addItem( "engine volume", CMD_ENGINE_VOL, Sound.getVolume(Sound.CHANNEL_ENGINE), null );
		m.addSeparator();
		soundHWMulti = m.addItem( "mixing", CMD_SOUND_HW, 0, soundHWText, null );

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_OPTIONS );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_OPTIONS, this );
		osd.hideGroup( soundGroup = osd.endGroup() );

		//---------controller options menu--------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "control options" );

		m = osd.createMenu( butt1, 0.0, -0.6, 0 );

		m.addItem( "redefine car controls", CMD_REDEFINE_CONTROLS );
		m.addItem( "redefine game controls", CMD_REDEFINE_CONTROLS2 );
		m.addItem( "advanced settings", CMD_REDEFINE_CONTROLS3 );
		m.addItem( "reset to defaults", CMD_RESET_CONTROLS );
		m.addItem( "load controls", CMD_LOAD_CONTROLS );
		m.addItem( "save controls", CMD_SAVE_CONTROLS );

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_OPTIONS );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_OPTIONS, this );
		osd.hideGroup( controlGroup = osd.endGroup() );

		//-------car controller options menu--------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "redefine car controls - driving" );

		m = osd.createMenu( Menu_Left, -0.98, -0.83, 0,  Osd.MD_HORIZONTAL );
		m.addItem( "driving", CMD_REDEFINE_CONTROLS );
		//m.addItem( "suspension", CMD_RED_SUSP );

		osd.createText( "    primary controls", Frontend.mediumFont, Text.ALIGN_LEFT, lpos, -0.7 );

		m = osd.createMenu( butt0, -0.95, -0.45, 0 );
		m.addItem( "ACCELERATE", CMD_GETKEY + 0 );
		m.addItem( "BRAKE", CMD_GETKEY + 1 );
		m.addItem( "TURN LEFT", CMD_GETKEY + 2 );
		m.addItem( "TURN RIGHT", CMD_GETKEY + 3 );
		m.addItem( "HAND BRAKE", CMD_GETKEY + 4 );
		m.addItem( "SHIFT UP", CMD_GETKEY + 5 );
		m.addItem( "SHIFT DOWN", CMD_GETKEY + 6 );
		m.addItem( "CLUTCH", CMD_GETKEY + 7 );
		m.addItem( "ENGAGE NOS", CMD_GETKEY + 8 );
		m.addItem( "HONK HORN", CMD_GETKEY + 9 );

		osd.createText( " secondary controls", Frontend.mediumFont, Text.ALIGN_LEFT, mpos, -0.7 );

		m = osd.createMenu( butt0, -0.1, -0.45, 0 );
		m.addItem( "ACCELERATE", CMD_GETKEY + 15 );
		m.addItem( "BRAKE", CMD_GETKEY + 16 );
		m.addItem( "TURN LEFT", CMD_GETKEY + 17 );
		m.addItem( "TURN RIGHT", CMD_GETKEY + 18 );
		m.addItem( "HAND BRAKE", CMD_GETKEY + 19 );
		m.addItem( "SHIFT UP", CMD_GETKEY + 20 );
		m.addItem( "SHIFT DOWN", CMD_GETKEY + 21 );
		m.addItem( "CLUTCH", CMD_GETKEY + 22 );
		m.addItem( "ENGAGE NOS", CMD_GETKEY + 23 );
		m.addItem( "HONK HORN", CMD_GETKEY + 24 );

		m = osd.createMenu( butt2, 0.98, 0.85, 0,  Osd.MD_HORIZONTAL );
		m.addItem( "back", CMD_REDEFINE_DONE );
		m.addItem( "check axis", CMD_AXISCHECK );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_REDEFINE_DONE, this );

		x = -0.40;
		y = -0.48;
		for (int i = 0; i < 25; i++)
		{
			if( i == 10 )
			{
				y += 0.6;
			}
			if( i == 15 )
			{
				x = 0.50;
				y = -0.48;
			}
			if (i < 10 || i > 14)
				keyText[i] = osd.createText ("undefined", Frontend.mediumFont, Text.ALIGN_LEFT, x, y);
			y += 0.12;
		}

		osd.hideGroup( redefineGroup = osd.endGroup() );

		//-------car pneumatis suspension--------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "redefine car controls - suspension" );
		osd.createRectangle( 0.0, 0.0, 1.65, 1.15, -1, new ResourceRef(MW_Mod:0x0097r) );//background car

		m = osd.createMenu( Menu_Left, -0.98, -0.83, 0,  Osd.MD_HORIZONTAL );
		m.addItem( "driving", CMD_REDEFINE_CONTROLS );
		m.addItem( "suspension", CMD_RED_SUSP );

		osd.createText( "redefine pneumatic suspension controls:", Frontend.mediumFont, Text.ALIGN_CENTER, 0.0, -0.7 );

		m = osd.createMenu( Menu_Center, 0.54, -0.38, 0 );
		m.addItem( "FL", CMD_GETKEY + 65 );
		keyText[65] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, 0.54, -0.32);

		m = osd.createMenu( Menu_Center, 0.54, -0.03, 0 );
		m.addItem( "Front", CMD_GETKEY + 66 );
		keyText[66] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, 0.54, 0.03);

		m = osd.createMenu( Menu_Center, 0.54, 0.32, 0 );
		m.addItem( "FR", CMD_GETKEY + 67 );
		keyText[67] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, 0.54, 0.38);

		m = osd.createMenu( Menu_Center, 0.0, -0.38, 0 );
		m.addItem( "Left", CMD_GETKEY + 68 );
		keyText[68] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, 0.0, -0.32);

		m = osd.createMenu( Menu_Center, 0.0, -0.03, 0 );
		m.addItem( "All", CMD_GETKEY + 69 );
		keyText[69] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, 0.0, 0.03);

		m = osd.createMenu( Menu_Center, 0.0, 0.32, 0 );
		m.addItem( "Right", CMD_GETKEY + 70 );
		keyText[70] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, 0.0, 0.38);

		m = osd.createMenu( Menu_Center, -0.54, -0.4, 0 );
		m.addItem( "RL", CMD_GETKEY + 71 );
		keyText[71] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, -0.54, -0.32);

		m = osd.createMenu( Menu_Center, -0.54, -0.03, 0 );
		m.addItem( "Rear", CMD_GETKEY + 72 );
		keyText[72] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, -0.54, 0.03);

		m = osd.createMenu( Menu_Center, -0.54, 0.32, 0 );
		m.addItem( "RR", CMD_GETKEY + 73 );
		keyText[73] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_CENTER, -0.54, 0.38);

		m = osd.createMenu( Menu_Right, 0.2, 0.7, 0 );
		m.addItem( "Change Mode (Up/Down)", CMD_GETKEY + 74 );
		keyText[74] = osd.createText ("Undefined", Frontend.mediumFont, Text.ALIGN_LEFT, 0.22, 0.66);

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_REDEFINE_DONE );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_REDEFINE_DONE, this );

		osd.hideGroup( redefineSuspGroup = osd.endGroup() );

		//-------game controller options menu--------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "redefine game controls" );

		m = osd.createMenu( butt0, -0.4, -0.6, 0 );

		//m.addItem( "access main menu", CMD_GETKEY + 10 );
		m.addItem( "next radio station", CMD_GETKEY + 11 );
		m.addItem( "prev radio station", CMD_GETKEY + 12 );
		m.addItem( "volume up", CMD_GETKEY + 13 );
		m.addItem( "volume down", CMD_GETKEY + 14 );
		m.addSeparator();
		m.addItem( "cruise control", CMD_GETKEY + 75 );
		m.addItem( "look back", CMD_GETKEY + 76 );
		m.addItem( "view map", CMD_GETKEY + 77 );

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_REDEFINE_DONE );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_REDEFINE_DONE, this );

		x =  0.30;
		y = -0.63;
		for (int i = 11; i <= 14; i++)
		{
			keyText[i] = osd.createText ("undefined", Frontend.mediumFont, Text.ALIGN_LEFT, x, y);
			y += 0.12;
		}

		y = -0.09;
		for (int i = 75; i <= 77; i++)
		{
			keyText[i] = osd.createText ("undefined", Frontend.mediumFont, Text.ALIGN_LEFT, x, y);
			y += 0.12;
		}

		osd.hideGroup( redefine2Group = osd.endGroup() );

		//-------------advanced settings menu----------------

		powerSlider = new Slider[4];
		dzSlider = new Slider[4];
		ffbSlider = new Slider[2];

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "advanced control settings" );

		m = osd.createMenu( butt1, 0.0, -0.6, 0 );
		m.setSliderStyle( sld1, sldk );

		powerSlider[0] = m.addItem( "accel. gamma", CMD_POWER + 0, 0.0, null );
		powerSlider[1] = m.addItem( "brake gamma", CMD_POWER + 1, 0.0, null );
		powerSlider[2] = m.addItem( "steering gamma", CMD_POWER + 2, 0.0, null );
		powerSlider[3] = m.addItem( "clutch gamma", CMD_POWER + 3, 0.0, null );

		m.addItem( "mouse sensitivity", CMD_MOUSE_SENS, Config.mouseSensitivity, null );

		dzSlider[0] = m.addItem( "accelerate dead zone", CMD_DEAD_ZONE + 0, 0.0, null );
		dzSlider[1] = m.addItem( "brake dead zone", CMD_DEAD_ZONE + 1, 0.0, null );
		dzSlider[2] = m.addItem( "steering dead zone", CMD_DEAD_ZONE + 2, 0.0, null );
		dzSlider[3] = m.addItem( "clutch dead zone", CMD_DEAD_ZONE + 3, 0.0, null );
		
		m.addSeparator();
		ffbSlider[0] = m.addItem( "real FFB strength", CMD_FFB + 0, Config.FFB_strength, 0.0f, 1.0f, 0, null );
		ffbSlider[1] = m.addItem( "emulated FFB strength", CMD_FFB + 1, Config.FFB_strength_emulated, 0.0f, 0.1f, 0, null );

		m = osd.createMenu( butt2, 0.98, 0.85, 0,  Osd.MD_HORIZONTAL );
		m.addItem( "back", CMD_REDEFINE_DONE );
		m.addItem( "check axes", CMD_AXISCHECK );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_REDEFINE_DONE, this );

		osd.hideGroup( redefine3Group = osd.endGroup() );

		//-------------axis checker menu----------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "view control axes" );
		osd.createText( "Please review how the axes respond to the car controls:", Frontend.mediumFont, Text.ALIGN_LEFT, -0.95, -0.7 );

		Style sldac = new Style( 0.85, 0.06, Frontend.mediumFont, Text.ALIGN_CENTER, new ResourceRef(Osd.RID_SLD_BACK) );

		m = osd.createMenu( butt1, 0.0, -0.45, 0 );
		m.setSliderStyle( sldac, sldk );

		acGasSlider = m.addItem( "throttle", CMD_UNUSED, 0.0, 0.0, 1.0, 0, null );
		acGasSlider.nofocus = 1;
		m.addSeparator();
		acBreakSlider = m.addItem( "brake", CMD_UNUSED, 0.0, 0.0, 1.0, 0, null );
		acBreakSlider.nofocus = 1;
		m.addSeparator();
		acSteerSlider = m.addItem( "steer", CMD_UNUSED, 0.5, 0.0, 1.0, 0, null );
		acSteerSlider.nofocus = 1;
		m.addSeparator();
		acClutchSlider = m.addItem( "clutch", CMD_UNUSED, 0.5, 0.0, 1.0, 0, null );
		acClutchSlider.nofocus = 1;

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_AXISCHECK_DONE );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_AXISCHECK_DONE, this );

		osd.hideGroup( axisCheckGroup = osd.endGroup() );

		//-------------game options menu----------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "game options" );

		m = osd.createMenu( butt0, 0.0, -0.6, 0 );
		m.setSliderStyle( sld1, sldk );

		m.addItem( "transmission", CMD_CLUTCH, clutchTrf[Config.player_transmission], clutchText, null );
		m.addItem( "metric system", CMD_METRIC, Config.metricSystem, metricSystemText, null );
		m.addItem( "GPS mode", CMD_GPSMODE, Config.gpsMode, gpsText, null );
		m.addItem( "damage level", CMD_DIFFICULTY, Config.deformation, 0.5, 1.5, 0, null );
		m.addSeparator();
		m.addItem( "steering help", CMD_STEERHELP, Config.player_steeringhelp, null );
		m.addItem( "ABS", CMD_ABS_SLIDER, Config.player_abs, null );
		m.addItem( "ASR", CMD_ASR_SLIDER, Config.player_asr, null );

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_GAME_OPTIONS_DONE );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_GAME_OPTIONS_DONE, this );
		osd.hideGroup( gameGroup = osd.endGroup() );

		//-------------game options 2 menu----------------

		osd.createBG( RID_GENERALBG );
		osd.createHeader( "advanced game options" );

		m = osd.createMenu( butt0, 0.0, -0.6, 0 );
		m.setSliderStyle( sld1, sldk );

		m.addItem( "driver movement/steer", CMD_HMF_1, Config.head_move_steer, 0.0, 2.0, 0, "HOW THE STEERING WHEELS POSITION AFFECTS THE DRIVERS MOVEMENT" );
		m.addItem( "driver movement/velocity", CMD_HMF_2, Config.head_move_vel, 0.0, 2.0, 0, "HOW THE CARS VELOCITY AFFECTS THE DRIVERS MOVEMENT"  );
		m.addItem( "driver movement/acceleratio", CMD_HMF_3, Config.head_move_acc, 0.0, 2.0, 0, "HOW THE CARS ACCELERATION AFFECTS THE DRIVERS MOVEMENT"  );
		m.addSeparator();
		m.addItem( "traffic density", CMD_TRAFFICDENSITY, Config.trafficDensity, 0.0, 1.5, 0, null );
		m.addItem( "pedestrian density", CMD_PEDESTRIANDENSITY, Config.pedestrianDensity, 0.0, 1.5, 0, null );

		m = osd.createMenu( butt2, 0.98, 0.85, 0 );
		m.addItem( "back", CMD_GAME_OPTIONS_DONE );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_GAME_OPTIONS_DONE, this );
		osd.hideGroup( game2Group = osd.endGroup() );

		super.show();

		enableAnimateHook();
	}

	public void hide()
	{
		disableAnimateHook();
		super.hide();
	}


	public void addCustomGroups()
	{
	}


//--------------------------------------------------------------------------------------
	public void setDetailText( Text txt, int detail )
	{
		if( detail == Config.OFF )
			txt.changeText( "Off" );
		else
		if( detail == Config.LOW )
			txt.changeText( "low" );
		else
		if( detail == Config.MID )
			txt.changeText( "mid" );
		else
		if( detail == Config.HIGH )
			txt.changeText( "high" );
		else
		if( detail == Config.SHIGH )
			txt.changeText( "super high" );
	}

	public void setEnabledText( Text txt, int enabled )
	{
		if( enabled == 0 )
			txt.changeText( "disabled" );
		else
			txt.changeText( "enabled" );
	}

	public int ChangeDetail (int value, int delta, int min, int max)
	{
		value += delta;
		if( value < min )
			value = max;
		if( value > max )
			value = min;
		return value;
	}

	public int fequal( float v1, float v2 )
	{
		float d = v1 - v2;
		if( d < 0 )
			d = -d;
		if( d < 0.001 )
			return 1;

		return 0;
	}

	public void osdCommand (int command)
	{
		if (command < 0)
			return;
		else
		if(command == CMD_COMPILE)
		{
			System.compileAll( "." );
			new SfxRef(sound:0x0023r).play(); // SFX
		}
		if (command == CMD_AXISCHECK)
		{
			axisCheckCallerGroup = actGroup;
			changeMode (axisCheckGroup);

			//a redefine menuk -ahonnan mi elerhetoek vagyunk- mar beallitottak, de meg nem mentettek a beallitasokat
			player.controller.setcontrol( controlSet );
		}
		else
		if (command == CMD_AXISCHECK_DONE)
		{
			changeMode (axisCheckCallerGroup);
		}
		else
		if (command == CMD_EFFECTS_VOL)
		{
			Sound.setVolume ( Sound.CHANNEL_EFFECTS, osd.sliderValue );
		}
		else
		if (command == CMD_MUSIC_VOL)
		{
			Sound.setVolume ( Sound.CHANNEL_MUSIC, osd.sliderValue );
		}
		else
		if (command == CMD_ENGINE_VOL)
		{
			Sound.setVolume ( Sound.CHANNEL_ENGINE, osd.sliderValue );
		}
		else
		if (command == CMD_OPTIONS)
		{
			changeMode (optionsGroup);
		}
		else
		if (command == CMD_VIDEO_OPTIONS)
		{
			changeMode (videoGroup);
		}
		else
		if (command == CMD_VIDEO_OPTIONS_DONE)
		{
			System.getConfigOptions();

			if(Config.video_x != aspectX || Config.video_y != aspectY || Config.video_depth != color_depth || Config.video_windowed != windowed )
			{
				int Correct_Mode = 0;

				if(Config.video_windowed == 1)
				{
					int d = ( 0 == new NoYesDialog( GameLogic.player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "WARNING", "Game restart is requiring to apply changes. \n Do you want make this now? \n \n Note that your current career will be saved." ).display() );

					Config.video_windowed = windowed;
					Config.video_x=aspectX;
					Config.video_y=aspectY;
					Config.video_depth=color_depth;

					if( d == 1 )
					{
						//if( carreerMode )
							GameLogic.autoSaveQuiet(); // Save

						GameLogic.changeActiveSection( null ); // Exit game
					}
					else
					{
						changeMode(optionsGroup);
					}
				}
				else
				{
					if(Config.video_windowed != windowed)
					{
						int d = ( 0 == new NoYesDialog( GameLogic.player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "WARNING", "Game restart is requiring to apply changes. \n Do you want make this now? \n \n Note that your current career will be saved." ).display() );

						Config.video_windowed = windowed;
						Config.video_x=aspectX;
						Config.video_y=aspectY;
						Config.video_depth=color_depth;

						if( d == 1 )
						{
							//if( carreerMode )
								GameLogic.autoSaveQuiet(); // Save

							GameLogic.changeActiveSection( null ); // Exit game
						}
						else
						{
							changeMode(optionsGroup);
						}
					}
					else
					{
						// check resolution
						for (int i=2; i<=ResX_list.length-1; i++ ) 
						{
							if(aspectX == ResX_list[i] && aspectY == ResY_list[i]) // found corect mode
							{
								Correct_Mode = 1;
								i += ResX_list.length; //end 'for' loop
							}
						}

						if( Correct_Mode )
						{
							Config.video_windowed=windowed;
							Config.video_x=aspectX;
							Config.video_y=aspectY;
							Config.video_depth=color_depth;

							GfxEngine.changeVideoMode( 320, 240, 16 ); // refresh for more compatible
							GfxEngine.changeVideoMode( 640, 480, 32 ); // refresh for more compatible
							GfxEngine.changeVideoMode( Config.video_x, Config.video_y, Config.video_depth );

							Frontend.setFonts();

							changeMode(optionsGroup);
						}
						else
						{
							new WarningDialog( GameLogic.player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "WARNING", "This resolution can't be use at full screen mode! \n \n Please change resolution or video mode." ).display();
						}
					}
				}
			}
			else
			{
				changeMode(optionsGroup);

				if( tmp_lp != loading_primary )
				{
					if( 0 == new NoYesDialog( GameLogic.player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "WARNING", "Game restart is requiring to apply changes of Loading Primary. \n Do you want make this now? \n \n Note that your current career will be saved." ).display() )
					{
						GameLogic.autoSaveQuiet(); // Save
						GameLogic.changeActiveSection( null ); // Exit game
					}
				}
			}
		}
		else
		if (command == CMD_M_GROUP)
		{
			changeMode(MirrorGroup);
		}
		if (command == CMD_RESOLUTION)
		{
			if(Current_Resolution < ResX_list.length-1)
				Current_Resolution += 1;
			else
				Current_Resolution = 0;

			aspectX = ResX_list[Current_Resolution];
			aspectY = ResY_list[Current_Resolution];

			osdCommand(CMD_REFRESH_TEXT);
		}
		else
		if (command == CMD_REFRESH_TEXT)
		{
			if(!aspectX)
			{
				aspectX = Config.video_x;
				aspectY = Config.video_y;
			}

			int TMPasp = 10*aspectX/aspectY;
			aspect = TMPasp;

			if(aspect == 10)	Res_Aspect = "1:1"; else
			if(aspect == 12)	Res_Aspect = "5:4"; else
			if(aspect == 13)	Res_Aspect = "4:3"; else
			if(aspect == 14)	Res_Aspect = "13:9"; else
			if(aspect == 15)	Res_Aspect = "14:9"; else
			if(aspect == 16)	Res_Aspect = "5:3"; else
			if(aspect == 17)	Res_Aspect = "16:9"; else
			if(aspect == 20)	Res_Aspect = "6:3"; else
			if(aspect == 23)	Res_Aspect = "12:5"; else
			if(aspect == 24)	Res_Aspect = "12:5"; else
			Res_Aspect = "OTHER";

			if(Resolution_button)
			{
				Resolution_button.changeLabelText( "screen resolution: "+Float.toString(aspectX, "%.0f")+" X "+Float.toString(aspectY, "%.0f")+" ("+Res_Aspect+")");
				Depth_button.changeLabelText("color depth: "+color_depth);
				
				if(windowed == 0)
					windowed_button.changeLabelText( "display mode: full screen" );
				else
					windowed_button.changeLabelText( "display mode: windowed" );
			}
		}
		else
		if(command == CMD_DEPTH)
		{
			if(Config.video_depth == 16)
				color_depth = 32;
			else
				color_depth = 16;
				
			Depth_button.changeLabelText("color depth: "+color_depth);
		}
		if(command == CMD_MODE)
		{
			if(windowed == 0)
			{
				windowed = 1;
				windowed_button.changeLabelText( "display mode: windowed" );
			}
			else
			{
				windowed = 0;
				windowed_button.changeLabelText( "display mode: full screen" );
			}
		}
		if(command == CMD_UNRES)
		{
			new UR_Dialog().display();
			osdCommand(CMD_REFRESH_TEXT);
		}
		if (command == CMD_TEXTURE_DETAIL)
		{
			textureDetail = osd.multiValue;
			Config.texture_size = 6-textureDetail;
		}
		else
		if (command == CMD_ABS_SLIDER)
		{
			Config.player_abs = osd.sliderValue;
		}
		else
		if (command == CMD_ASR_SLIDER)
		{
			Config.player_asr = osd.sliderValue;
		}
		else
		if (command == CMD_HMF_1)
		{
			Config.head_move_steer = osd.sliderValue;
		}
		else
		if (command == CMD_HMF_2)
		{
			Config.head_move_vel = osd.sliderValue;
		}
		else
		if (command == CMD_HMF_3)
		{
			Config.head_move_acc = osd.sliderValue;
		}
		else
		if (command == CMD_SHADOW_DETAIL)
		{
			shadow_size = osd.multiValue;

			if( shadow_size == 7 )
			{
				Config.shadow_size = 8192;
				Config.shadows = 2;
			}
			else
			if( shadow_size == 6 )
			{
				Config.shadow_size = 4096;
				Config.shadows = 2;
			}
			else
			if( shadow_size == 5 )
			{
				Config.shadow_size = 2048;
				Config.shadows = 2;
			}
			else
			if( shadow_size == 4 )
			{
				Config.shadow_size = 1024;
				Config.shadows = 2;
			}
			else
			if( shadow_size == 3 )
			{
				Config.shadow_size = 512;
				Config.shadows = 2;
			}
			else
			if( shadow_size == 2 )
			{
				Config.shadow_size = 256;
				Config.shadows = 1;
			}
			else
			if( shadow_size == 1 )
			{
				Config.shadow_size = 128;
				Config.shadows = 1;
			}
			else
			{
				Config.shadow_size = 64;
				Config.shadows = 0;
			}
			//CMD_VIDEO_OPTIONS_DONE hatasara lep ervenybe!
			refreshCameras();
		}
		else
		if (command == CMD_SHADOW_RANGE)
		{
			Config.shadow_detail = 1/osd.sliderValue;
			Config.shadow_detail = Config.shadow_detail*10;
			ShadowDist_Text.changeLabelText("shadow view distance: " + Float.toString(1/Config.shadow_detail*10, "%.0f m") );

			refreshCameras();
		}
		else
		if(command == CMD_LP)
		{
			loading_primary = osd.multiValue;

			if(loading_primary == 0) // LOW
			{
				Config.resource_refreshrate = 2;
				Config.resource_loadrate = 10;
			}
			else
			if(loading_primary == 1) // MEDIUM
			{
				Config.resource_refreshrate = 512;
				Config.resource_loadrate = 512;
			}
			if(loading_primary == 2) // HIGHT
			{
				Config.resource_refreshrate = 2048;
				Config.resource_loadrate = 512;
			}
		}
		if (command == CMD_FLARES)
		{
			Config.flares = osd.multiValue;
			refreshCameras();
		}
		else
		if (command == CMD_HEADLIGHTS)
		{
			Config.headlight_rays = osd.multiValue;
			refreshCameras();
		}
		else
		if (command == CMD_BGSCENE)
		{
			Config.BgScene = osd.multiValue;
		}
		else
		if (command == CMD_MIRROR)
		{
			Config.mirror = osd.multiValue;
		}
		else
		if (command == CMD_VIEW_RANGE)
		{
			Config.cameras_viewrange = osd.sliderValue;
			Config.camera_ext_viewrange = osd.sliderValue;
			Config.camera_int_viewrange = osd.sliderValue;
			Dist_Text.changeLabelText("view distance: " + Float.toString(Config.camera_ext_viewrange, "%.0f m"));
			refreshCameras();
		}
		else
		if (command == CMD_MIRROR_DISTANCE)
		{
			Config.camera_mirror_viewrange = osd.sliderValue;
			MirrorDist_Text.changeLabelText("mirror view distance: " + Float.toString(Config.camera_mirror_viewrange, "%.0f m"));
			refreshCameras();
		}
		else
		if (command == CMD_OBJECT_DETAIL)
		{
			Config.object_detail = osd.sliderValue;
			refreshCameras();
		}
		else
		if (command == CMD_LOD_DETAIL)
		{
			Config.object_detail_amp = osd.sliderValue;
			refreshCameras();
		}
		else
		if (command == CMD_PARTICLE)
		{
			Config.particle_density = osd.sliderValue;
			Particle_Text.changeLabelText("particle density: " + Float.toString(Config.particle_density*100, "%.0f")+"%");
			refreshCameras();
		}
		else
		if (command == CMD_GAMMA)
		{
			Config.video_gamma = osd.sliderValue;
			refreshCameras();
		}
		else
		if (command == CMD_CONTROL_OPTIONS)
		{
			changeMode (controlGroup);
		}
		else
		if (command == CMD_GAME_OPTIONS)
		{
			changeMode (gameGroup);
		}
		else
		if (command == CMD_GAME_OPTIONS2)
		{
			changeMode (game2Group);
		}
		else
		if (command == CMD_GPSMODE)
		{
			Config.gpsMode = osd.multiValue;
		}
		if (command == CMD_METRIC)
		{
			Config.metricSystem = osd.multiValue;		// miles, kilometers, 
			if( Config.metricSystem == 0 )
				System.setMeasure( 1600 );
			else
				System.setMeasure( 1000 );
		}
		else
		if (command == CMD_DIFFICULTY)
		{
			Config.deformation = osd.sliderValue;
			Config.internal_damage = osd.sliderValue;
			Config.player_damage_multiplier = osd.sliderValue / 2.0;
		}
		else
		if (command == CMD_TRAFFICDENSITY)
		{
			Config.trafficDensity = osd.sliderValue;
		}
		else
		if (command == CMD_PEDESTRIANDENSITY)
		{
			Config.pedestrianDensity = osd.sliderValue;
		}
		if (command == CMD_SKIDMARK)
		{
			Config.skidmark_max = osd.sliderValue * 4.0;
			SkidMark_Text.changeLabelText("SKIDMARKS LENGTH: "+Config.skidmark_max/4+" m");
		}
		else
		if (command == CMD_CLUTCH)
		{
			Config.player_transmission = clutchTrfInv[osd.multiValue];
		}
		else
		if (command == CMD_STEERHELP)
		{
			Config.player_steeringhelp = osd.sliderValue;
		}
		else
		if (command == CMD_GAME_OPTIONS_DONE)
		{
			System.getConfigOptions();
			changeMode (optionsGroup);
		}
		else
		if (command == CMD_SOUND_OPTIONS)
		{
			//lehetseges modeok, es kijelzesuk
			//MIX	OFF	ON	ON	AUTO
			//3D	OFF	OFF	ON	AUTO
			//		OFF	2D	3D	AUTO

			int soundHW = Config.Sound_Mix_HW;
			if( soundHW == 2 )
				soundHW++;
			if( Config.Sound_Mix_HW == 1 && Config.Sound_3D_HW == 1 )
				soundHW++;

			soundHWMulti.setValue( soundHW );

			changeMode (soundGroup);
		}
		else
		if (command == CMD_SOUND_HW)
		{
			switch( osd.multiValue )
			{
			case 0:
				Config.Sound_Mix_HW = 0;
				Config.Sound_3D_HW = 0;
				break;
			case 1:
				Config.Sound_Mix_HW = 1;
				Config.Sound_3D_HW = 0;
				break;
			case 2:
				Config.Sound_Mix_HW = 1;
				Config.Sound_3D_HW = 1;
				break;
			case 3:
				Config.Sound_Mix_HW = 2;
				Config.Sound_3D_HW = 2;
				break;
			}
			//TODO: tell the sound engine to apply the changes
		}
		else
		if (command == CMD_MAIN)
		{
			changeMode (mainGroup);
		}
		else
		if (command == CMD_RED_SUSP || command == CMD_REDEFINE_CONTROLS || command == CMD_REDEFINE_CONTROLS2 || command == CMD_REDEFINE_CONTROLS3)
		{
			int	i;

			controlSet = new ControlSet();
			controlSet.load( GameLogic.activeControlFile );

			for( int i = 0; i < NCONTROLS; i++ )
			{
				if( keyText[i] )
				{
					if (controlSet.deviceID[i] < 0 || controlSet.axisID[i] < 0)
						keyText[i].changeText( "undefined" );
					else
						keyText[i].changeText( Input.axisName (controlSet.deviceID[i], controlSet.axisID[i]) );
				}
			}

			powerSlider[0].setValue( (controlSet.vasp.elementAt(0).power - 1.0) / 4.0 );	// value: 1.0..5.0
			powerSlider[1].setValue( (controlSet.vasp.elementAt(1).power - 1.0) / 4.0 );
			powerSlider[2].setValue( (controlSet.vasp.elementAt(2).power - 1.0) / 4.0 );
			powerSlider[3].setValue( (controlSet.vasp.elementAt(3).power - 1.0) / 4.0 );

			dzSlider[0].setValue( controlSet.dead_zone[0] / 0.2 );							// value: 0.0..0.2
			dzSlider[1].setValue( controlSet.dead_zone[1] / 0.2 );
			dzSlider[2].setValue( controlSet.dead_zone[2] / 0.2 );
			dzSlider[3].setValue( controlSet.dead_zone[7] / 0.2 );

			if(command == CMD_REDEFINE_CONTROLS)
				changeMode (redefineGroup);
			else 
			if(command == CMD_REDEFINE_CONTROLS2)
				changeMode (redefine2Group);
			else 
			if(command == CMD_RED_SUSP)
				changeMode (redefineSuspGroup);
			else
				changeMode (redefine3Group);
		}
		else
		if (command == CMD_REDEFINE_DONE)
		{
			controlSet.save( GameLogic.activeControlFile );
			changeMode (controlGroup);
		}
		else
		if (command == CMD_RESET_CONTROLS)
		{
			controlSet = new ControlSet();
			controlSet.save( GameLogic.activeControlFile );
			player.controller.setcontrol( controlSet );
		}
		else
		if (command == CMD_LOAD_CONTROLS)
		{
			ControlsFileReqDialog d = new ControlsFileReqDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "LOAD CONTROL DEFINITIONS", "LOAD CONTROLS", GameLogic.controlSaveDir, "*" );

			if( d.display() == 0 )
			{
				String filename = GameLogic.controlSaveDir + d.fileName;

				controlSet = new ControlSet();
				controlSet.load( filename );
				controlSet.save( GameLogic.activeControlFile );
				player.controller.setcontrol( controlSet );
			}
		}
		else
		if (command == CMD_SAVE_CONTROLS)
		{
			ControlsFileReqDialog d = new ControlsFileReqDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG|Dialog.FRF_SAVE, "SAVE CONTROL DEFINITIONS", "SAVE CONTROLS", GameLogic.controlSaveDir, "*" );

			if( d.display() == 0 )
			{
				String filename = GameLogic.controlSaveDir + d.fileName;

				if( File.exists( GameLogic.activeControlFile ) )
				{
					File.copy( GameLogic.activeControlFile, filename );
				}
				else
				{	//elso inditas, meg nem letezik a file!!
					controlSet = new ControlSet();
					controlSet.save( filename );
				}
			}
		}
		else
		if (command >= CMD_GETKEY && command < CMD_GETKEY + NCONTROLS)
		{
			if (getKey == -1)
			{
				getKey = command - CMD_GETKEY;
				keyText[getKey].changeText("_");
				getKeyStat = 0;
				// special request: reset mouse and set sensitivity
				Input.getAxis (1, -1 - (Config.mouseSensitivity * 100.0f));
			}
		}
		else
		if (command >= CMD_DEAD_ZONE && command < CMD_DEAD_ZONE + 4)
		{
			int		dz;

			dz = command - CMD_DEAD_ZONE;	// remap 0,1,2,3 -> 0,1,2,7
			if (dz == 3)
			{
				dz = 7;
			}
			controlSet.dead_zone[dz] = osd.sliderValue * 0.2f;
			controlSet.dead_zone[dz + 15] = osd.sliderValue * 0.2f;
			player.controller.setcontrol( controlSet );
		}
		else
		if (command >= CMD_POWER && command < CMD_POWER + 4)
		{
			float pow = 1.125 + 4.0 * osd.sliderValue;
			int ipower = pow * 4;
			pow = ipower;
			pow = pow / 4.0;
			controlSet.vasp.elementAt( command - CMD_POWER ).power = pow;
			player.controller.setcontrol( controlSet );
		}
		else
		if (command == CMD_FFB + 0)
		{
			Config.FFB_strength = osd.sliderValue;
			System.getConfigOptions();
		} 
		else
		if (command == CMD_FFB + 1)
		{
			Config.FFB_strength_emulated = osd.sliderValue;
			System.getConfigOptions();
		} 
		else
		if (command == CMD_MOUSE_SENS)
		{
			Config.mouseSensitivity = osd.sliderValue;
			if (Config.mouseSensitivity < 0.1)
				Config.mouseSensitivity = 0.1;
			// special request: reset mouse and set sensitivity
			Input.getAxis (1, -1 - (Config.mouseSensitivity * 100.0f));
			//send command to cursor
			Input.cursor.config();
		}
	}

	public void changeMode(int group)
	{
		if( actGroup != group )
		{
			if (actGroup >= 0)
			{
				osd.hideGroup (actGroup);
			}

			actGroup = group;

			osd.showGroup (actGroup);

			osd.changeSelection2( -1, 0 );

			if(actGroup == videoGroup)
				osdCommand(CMD_REFRESH_TEXT);
		}
	}


	public void animate()
	{
		//control remapper
		if (actGroup == redefineGroup || actGroup == redefine2Group || actGroup == redefineSuspGroup)
		{
			if (getKey >= 0 && getKey < NCONTROLS)
			{
				int		device;
				int		axis = -1;

				for( device = 0; device < controlSet.nDevices; device++ )
				{
					axis = Input.activeAxis (device);
					if( axis >= 0 )
						break;
				}

				if (getKeyStat == 0) 
				{
					if (axis < 0) 
					{
						getKeyStat = 1;
					}
				} 
				else 
				if (getKeyStat == 1)
				{
					if( device == 0 && axis == Input.RCDIK_ESCAPE )
					{
						keyText[getKey].changeText( "undefined" );
						controlSet.change( getKey, -1, -1 );
						player.controller.setcontrol( controlSet );
						getKeyStat = 2;
					}
					else
					{
						if (axis >= 0) 
						{
							float val = Input.getAxis (device, axis);
							keyText[getKey].changeText (Input.axisName (device, axis));
							controlSet.change( getKey, device, axis);
							int i;
							for (i = 0; i < NCONTROLS; i++) 
							{
								if (i != getKey && controlSet.deviceID [i] == device && controlSet.axisID [i] == axis)
									break;
							}
							if (device == 0 || val > 0.95f) 
							{
								controlSet.from_min [getKey] = 0.0f;
								controlSet.from_max [getKey] = 1.0f;
							} 
							else 
							{
								if (i < NCONTROLS) 
								{
									if (val < 0.0f) 
									{
										controlSet.from_min [getKey] = 0.0f;
										controlSet.from_max [getKey] = -1.0f;
										controlSet.from_min [i] = 0.0f;
										controlSet.from_max [i] = 1.0f;
									} 
									else 
									{
										controlSet.from_min [getKey] = 0.0f;
										controlSet.from_max [getKey] = 1.0f;
										controlSet.from_min [i] = 0.0f;
										controlSet.from_max [i] = -1.0f;
									}
								} 
								else 
								{
									controlSet.from_min [getKey] = -1.0f;
									controlSet.from_max [getKey] = 1.0f;
								}
							}
							player.controller.setcontrol( controlSet );
							getKeyStat = 2;
						}
					}
				} 
				else 
				{
					if (axis < 0) 
					{
						getKey = -1;
					}
				}
			}
		}

		//gas/brake/steer axis position updater
		if (actGroup == axisCheckGroup)
		{
			acGasSlider.setValue( Input.getInput( Input.AXIS_THROTTLE ) / 128.0 );
			acBreakSlider.setValue( Input.getInput( Input.AXIS_BRAKE ) / 128.0 );
			acSteerSlider.setValue( Input.getInput( Input.AXIS_TURN_LEFTRIGHT ) / -256.0 + 0.5);
			acClutchSlider.setValue( Input.getInput( Input.AXIS_CLUTCH ) / 128.0);
		}
	}


	GameRef activeCamera;

	public void refreshCameras()
	{
		System.getConfigOptions();

		//mega patchek:
		//( a fenti hivasnak kellene triggerelni minden aktiv 3d-engine kamerat, nem itt vacakolni veluk!!!)
		if( activeCamera )
			activeCamera.command( "render 0" );

		if (GameLogic.player && GameLogic.player.car )
			GameLogic.player.car.command( "render 0 "+ GameLogic.player.controller.id() );

	}

	public void setActiveCamera( GameRef cam )
	{
		activeCamera = cam;
	}

}


class ControlsFileReqDialog extends FileRequesterDialog
{
	public ControlsFileReqDialog( Controller ctrl, int myflags, String mytitle, String OKButtonText, String path, String mask )
	{
		super( ctrl, myflags, mytitle, OKButtonText, path, mask );
		if( myflags & FRF_SAVE )
			osd.defSelection = 5;
	}

	public int validator( String filename )
	{
		return ControlSet.fileCheck( filename );
	}
}

class UR_Dialog extends Dialog
{
	final static int	CMD_BACK = 15;
	final static int	CMD_OK	 = 16;

	final static int	CMD_X_UP = 20;
	final static int	CMD_X_DOWN = 21;

	final static int	CMD_Y_UP = 22;
	final static int	CMD_Y_DOWN = 23;

	Menu	m;
	Style	Menu_Center, Menu_Button;
	Text	ResText, AspectText;
	String	Res_Aspect;

	int[]	Res_list;
	int		SizeX, SizeY, numberX, numberY;
	float	aspect;

	public UR_Dialog()
	{
		super( GameLogic.player.controller, DF_MODAL|DF_DEFAULTBG|DF_HIGHPRI|DF_DARKEN, "uncommon resolution", "" );

		Res_list = new int[24];
		Res_list[0] = 120;
		Res_list[1] = 160;
		Res_list[2] = 200;
		Res_list[3] = 213;
		Res_list[4] = 240;
		Res_list[5] = 300;
		Res_list[6] = 320;
		Res_list[7] = 360;
		Res_list[8] = 384;
		Res_list[9] = 400;
		Res_list[10] = 480;
		Res_list[11] = 512;
		Res_list[12] = 576;
		Res_list[13] = 600;
		Res_list[14] = 640;
		Res_list[15] = 720;
		Res_list[16] = 768;
		Res_list[17] = 800;
		Res_list[18] = 848;
		Res_list[19] = 864;
		Res_list[20] = 960;
		Res_list[21] = 1024;
		Res_list[22] = 1080;
		Res_list[23] = 1152;
		Res_list[24] = 1280;
		Res_list[25] = 1920;

		for (int i=0; i<=Res_list.length-1; i++ ) // Get X Res
		{
			if(OptionsDialog.aspectX == Res_list[i])
			{
				numberX = i;
				i += Res_list.length; //end 'for' loop
			}
		}

		for (int i=0; i<=Res_list.length-1; i++ ) // Get Y Res
		{
			if(OptionsDialog.aspectY == Res_list[i])
			{
				numberY = i;
				i += Res_list.length; //end 'for' loop
			}
		}
		
		Menu_Center = new Style( 0.50, 0.13, Frontend.mediumFont, Text.ALIGN_CENTER, Osd.RRT_TEST );
		Menu_Button = new Style( 0.12, 0.12, Frontend.mediumFont, Text.ALIGN_CENTER, null );

		ResText = osd.createText( "----   X   ----" , Frontend.largeFont, Text.ALIGN_CENTER, 0.0, -0.3 ); ResText.changeColor(0xFFC0C0C0);// "Unselected" gray

		m = osd.createMenu( Menu_Button, -0.5, -0.5, 0, Osd.MD_VERTICAL );
		m.addItem( new ResourceRef( frontend:0x004Cr ), CMD_X_UP, "Increase X size" );

		m = osd.createMenu( Menu_Button, -0.5, 0.0, 0, Osd.MD_VERTICAL );
		m.addItem( new ResourceRef( frontend:0x0084r ), CMD_X_DOWN, "Decrease X size" );

		m = osd.createMenu( Menu_Button, 0.5, -0.5, 0, Osd.MD_VERTICAL );
		m.addItem( new ResourceRef( frontend:0x004Cr ), CMD_Y_UP, "Increase Y size" );

		m = osd.createMenu( Menu_Button, 0.5, 0.0, 0, Osd.MD_VERTICAL );
		m.addItem( new ResourceRef( frontend:0x0084r ), CMD_Y_DOWN, "Decrease Y size" );

		AspectText = osd.createText( "Aspect: N/A" , Frontend.largeFont, Text.ALIGN_CENTER, 0.0, 0.4 ); AspectText.changeColor(0xFFC0C0C0);// "Unselected" gray

		m = osd.createMenu( Menu_Center, -0.4, 0.9, 0, Osd.MD_HORIZONTAL );
		m.addItem( "OK", CMD_OK );
		
		m = osd.createMenu( Menu_Center, 0.4, 0.9, 0, Osd.MD_HORIZONTAL );
		m.addItem( "back", CMD_BACK );

		Refresh();

		super.show();
	}

	public void Refresh()
	{
		float x = Res_list[numberX];
		float y = Res_list[numberY];

		aspect = x/y;
		int TMPasp = 10*aspect;

		if(TMPasp == 10)	Res_Aspect = "1:1"; else
		if(TMPasp == 12)	Res_Aspect = "5:4"; else
		if(TMPasp == 13)	Res_Aspect = "4:3"; else
		if(TMPasp == 14)	Res_Aspect = "13:9"; else
		if(TMPasp == 15)	Res_Aspect = "14:9"; else
		if(TMPasp == 16)	Res_Aspect = "5:3"; else
		if(TMPasp == 17)	Res_Aspect = "16:9"; else
		if(TMPasp == 20)	Res_Aspect = "6:3"; else
		if(TMPasp == 23)	Res_Aspect = "12:5"; else
		if(TMPasp == 24)	Res_Aspect = "12:5"; else
		Res_Aspect = "OTHER";

		ResText.changeText( Res_list[numberX] + "   X   " + Res_list[numberY] );
		AspectText.changeText( "Aspect: " + aspect + " (" + Res_Aspect + ")" );
	}
		
	public void osdCommand( int cmd )
	{
		if( cmd == CMD_X_DOWN )
		{
			if( numberX > 0)
				numberX -= 1;
			else
				numberX = Res_list.length-1;

			Refresh();
		}
		else
		if( cmd == CMD_X_UP )
		{
			if( numberX < Res_list.length-1)
				numberX += 1;
			else
				numberX = 0;

			Refresh();
		}
		else
		if( cmd == CMD_Y_DOWN )
		{
			if( numberY > 0)
				numberY -= 1;
			else
				numberY = Res_list.length-1;

			Refresh();
		}
		else
		if( cmd == CMD_Y_UP )
		{
			if( numberY < Res_list.length-1)
				numberY += 1;
			else
				numberY = 0;

			Refresh();
		}
		else
		if( cmd == CMD_BACK )
		{
			osd.hide();
			super.hide();
		}
		else
		if( cmd == CMD_OK )
		{
			OptionsDialog.aspectX = Res_list[numberX];
			OptionsDialog.aspectY = Res_list[numberY];

			osd.hide();
			super.hide();
		}
	}
}
