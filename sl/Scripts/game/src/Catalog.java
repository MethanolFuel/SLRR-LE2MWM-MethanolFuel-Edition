package java.game;

import java.io.*;
import java.util.*;
import java.util.lang.*;
import java.util.resource.*;
import java.render.*;
import java.render.osd.*;
import java.render.osd.dialog.*;
import java.sound.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.rgearpart.reciprocatingrgearpart.*;

public class Catalog extends GameType implements GameState
{
	final static ResourceRef RR_ICON_CAR_COMP = new ResourceRef( misc.catalog:0x0004r );
	final static ResourceRef RR_ICON_CAR_1STEP = new ResourceRef( misc.catalog:0x0005r );
	final static ResourceRef RR_ICON_INV_COMP = new ResourceRef( misc.catalog:0x0007r );


	final static int DECALPRICE = 20;

	final static int SC_ENGINE	= 0;
	final static int SC_BODY	= 1;
	final static int SC_RGEAR	= 2;
	final static int SC_NONE	= 3;	//max+1

	final static int CMD_EXIT  = 0;
	final static int CMD_FRONTPAGE = 1;
	final static int CMD_MAIN1 = 2;
	final static int CMD_MAIN2 = 3;
	final static int CMD_MAIN3 = 4;
	final static int CMD_MAIN4 = 5;
	final static int CMD_MAIN5 = 6;
	final static int CMD_MAIN6 = 7;

	final static int CMD_MAIN1_1 = 8;
	final static int CMD_MAIN1_2 = 9;
	final static int CMD_MAIN1_3 =10;
	final static int CMD_MAIN1_4 =11;
	final static int CMD_MAIN1_5 =12;
	final static int CMD_MAIN1_6 =13;
	final static int CMD_MAIN1_7 =14;
	final static int CMD_MAIN1_8 =15;

	final static int CMD_MAIN1_1_1 =16;
	final static int CMD_MAIN1_1_2 =17;
	final static int CMD_MAIN1_1_3 =18;
	final static int CMD_MAIN1_1_4 =19;
	final static int CMD_MAIN1_1_5 =20;
	final static int CMD_MAIN1_1_6 =21;
	final static int CMD_MAIN1_1_7 =22;
	final static int CMD_MAIN1_1_8 =23;
	final static int CMD_MAIN1_1_9 =24;
	final static int CMD_MAIN1_1_10 =25;
	final static int CMD_MAIN1_1_11 =26;
	final static int CMD_MAIN1_1_12 =27;
	final static int CMD_MAIN1_1_13 =28;
	final static int CMD_MAIN1_1_14 =29;

	final static int CMD_MAIN1_2_1 =30;
	final static int CMD_MAIN1_2_2 =31;
	final static int CMD_MAIN1_2_3 =32;
	final static int CMD_MAIN1_2_4 =33;
	final static int CMD_MAIN1_2_5 =34;
	final static int CMD_MAIN1_2_6 =35;
	final static int CMD_MAIN1_2_7 =36;
	final static int CMD_MAIN1_2_8 =37;
	final static int CMD_MAIN1_2_9 =38;
	final static int CMD_MAIN1_2_10 =39;
	final static int CMD_MAIN1_2_11 =40;
	final static int CMD_MAIN1_2_12 =41;
	final static int CMD_MAIN1_2_13 =42;
	final static int CMD_MAIN1_2_14 =43;

	final static int CMD_MAIN1_3_1 =44;
	final static int CMD_MAIN1_3_2 =45;
	final static int CMD_MAIN1_3_3 =46;
	final static int CMD_MAIN1_3_4 =47;
	final static int CMD_MAIN1_3_5 =48;
	final static int CMD_MAIN1_3_6 =49;
	final static int CMD_MAIN1_3_7 =50;
	final static int CMD_MAIN1_3_8 =51;
	final static int CMD_MAIN1_3_9 =52;
	final static int CMD_MAIN1_3_10 =53;
	final static int CMD_MAIN1_3_11 =54;
	final static int CMD_MAIN1_3_12 =55;
	final static int CMD_MAIN1_3_13 =56;
	final static int CMD_MAIN1_3_14 =57;

	final static int CMD_MAIN1_4_1 =58;
	final static int CMD_MAIN1_4_2 =59;
	final static int CMD_MAIN1_4_3 =60;
	final static int CMD_MAIN1_4_4 =61;
	final static int CMD_MAIN1_4_5 =62;
	final static int CMD_MAIN1_4_6 =63;
	final static int CMD_MAIN1_4_7 =64;
	final static int CMD_MAIN1_4_8 =65;
	final static int CMD_MAIN1_4_9 =66;
	final static int CMD_MAIN1_4_10 =67;
	final static int CMD_MAIN1_4_11 =68;
	final static int CMD_MAIN1_4_12 =69;
	final static int CMD_MAIN1_4_13 =70;
	final static int CMD_MAIN1_4_14 =71;

	final static int CMD_MAIN1_5_1 =72;
	final static int CMD_MAIN1_5_2 =73;
	final static int CMD_MAIN1_5_3 =74;
	final static int CMD_MAIN1_5_4 =75;
	final static int CMD_MAIN1_5_5 =76;
	final static int CMD_MAIN1_5_6 =77;
	final static int CMD_MAIN1_5_7 =78;
	final static int CMD_MAIN1_5_8 =79;
	final static int CMD_MAIN1_5_9 =80;
	final static int CMD_MAIN1_5_10 =81;
	final static int CMD_MAIN1_5_11 =82;
	final static int CMD_MAIN1_5_12 =83;
	final static int CMD_MAIN1_5_13 =84;
	final static int CMD_MAIN1_5_14 =85;

	final static int CMD_MAIN1_6_1 =86;
	final static int CMD_MAIN1_6_2 =87;
	final static int CMD_MAIN1_6_3 =88;
	final static int CMD_MAIN1_6_4 =89;
	final static int CMD_MAIN1_6_5 =90;
	final static int CMD_MAIN1_6_6 =91;
	final static int CMD_MAIN1_6_7 =92;
	final static int CMD_MAIN1_6_8 =93;
	final static int CMD_MAIN1_6_9 =94;
	final static int CMD_MAIN1_6_10 =95;
	final static int CMD_MAIN1_6_11 =96;
	final static int CMD_MAIN1_6_12 =97;
	final static int CMD_MAIN1_6_13 =98;
	final static int CMD_MAIN1_6_14 =99;

	final static int CMD_MAIN1_7_1 =100;
	final static int CMD_MAIN1_7_2 =101;
	final static int CMD_MAIN1_7_3 =102;
	final static int CMD_MAIN1_7_4 =103;
	final static int CMD_MAIN1_7_5 =104;
	final static int CMD_MAIN1_7_6 =105;
	final static int CMD_MAIN1_7_7 =106;
	final static int CMD_MAIN1_7_8 =107;
	final static int CMD_MAIN1_7_9 =108;
	final static int CMD_MAIN1_7_10 =109;
	final static int CMD_MAIN1_7_11 =110;
	final static int CMD_MAIN1_7_12 =111;
	final static int CMD_MAIN1_7_13 =112;
	final static int CMD_MAIN1_7_14 =113;

	final static int CMD_MAIN2_1 =114;
	final static int CMD_MAIN2_2 =115;
	final static int CMD_MAIN2_3 =116;
	final static int CMD_MAIN2_4 =117;
	final static int CMD_MAIN2_5 =118;
	final static int CMD_MAIN2_6 =119;
	final static int CMD_MAIN2_7 =120;

	final static int CMD_MAIN3_1 =121;
	final static int CMD_MAIN3_2 =122;
	final static int CMD_MAIN3_3 =123;
	final static int CMD_MAIN3_4 =124;
	final static int CMD_MAIN3_5 =125;
	final static int CMD_MAIN3_6 =126;
	final static int CMD_MAIN3_7 =127;
	final static int CMD_MAIN3_8 =128;
	final static int CMD_MAIN3_9 =129;
	final static int CMD_MAIN3_10 =130;
	final static int CMD_MAIN3_11 =131;
	final static int CMD_MAIN3_12 =132;
	final static int CMD_MAIN3_13 =133;
	final static int CMD_MAIN3_14 =134;
	final static int CMD_MAIN3_15 =135;
	final static int CMD_MAIN3_16 =136;
	final static int CMD_MAIN3_17 =137;
	final static int CMD_MAIN3_18 =138;
	final static int CMD_MAIN3_19 =139;
	final static int CMD_MAIN3_20 =140;
	final static int CMD_MAIN3_21 =141;
	final static int CMD_MAIN3_22 =142;
	final static int CMD_MAIN3_23 =143;
	final static int CMD_MAIN3_24 =144;
	final static int CMD_MAIN3_25 =145;
	final static int CMD_MAIN3_26 =146;
	final static int CMD_MAIN3_27 =147;
	final static int CMD_MAIN3_28 =148;

	final static int CMD_MAIN4_1 =149;
	final static int CMD_MAIN4_2 =150;
	final static int CMD_MAIN4_3 =151;
	final static int CMD_MAIN4_4 =152;
	final static int CMD_MAIN4_5 =153;
	final static int CMD_MAIN4_6 =154;
	final static int CMD_MAIN4_7 =155;
	final static int CMD_MAIN4_8 =156;

	final static int CMD_MAIN5_1 =157;
	final static int CMD_MAIN5_2 =158;
	final static int CMD_MAIN5_3 =159;
	final static int CMD_MAIN5_4 =160;
	final static int CMD_MAIN5_5 =161;
	final static int CMD_MAIN5_6 =162;

	final static int CMD_MAIN6_1 =163;
	final static int CMD_MAIN6_2 =164;
	final static int CMD_MAIN6_3 =165;
	final static int CMD_MAIN6_4 =166;
	final static int CMD_MAIN6_5 =167;
	final static int CMD_MAIN6_6 =168;
	final static int CMD_MAIN6_7 =169;


	final static int CMD_DB0 =170;
	final static int CMD_DB1 =171;
	final static int CMD_DB2 =172;
	final static int CMD_DB3 =173;
	final static int CMD_DB4 =174;
	final static int CMD_DB5 =175;
	final static int CMD_DB6 =176;
	final static int CMD_DB7 =177;

	final static int CMD_PREVDECALPAGE =178;
	final static int CMD_NEXTDECALPAGE =179;

	final static int CMD_PREVPARTPAGE =180;
	final static int CMD_NEXTPARTPAGE =181;

// Chassis shop
	final static int CMD_VEHICLE =200;
	final static int CMD_VEH0	 =201;
	final static int CMD_VEH1	 =202;
	final static int CMD_VEH2	 =203;
	final static int CMD_VEH3	 =204;
	final static int CMD_VEH4	 =205;
	final static int CMD_VEH5	 =206;
	final static int CMD_VEH6	 =207;
	final static int CMD_VEH7	 =208;
	final static int CMD_VEH8	 =209;
	final static int CMD_VEH9	 =210;
	final static int CMD_UP		 =211;
	final static int CMD_DOWN	 =212;
	final static int CMD_VEHI	 =213;
	final static int CMD_CHAS	 =214;
	final static int CMD_VBUY	 =215;
	final static int CMD_VCOLOR	 =216;

	final static int NO_FILTER	 =220;
	final static int RIMS_FILTER	 =221;
	final static int TIRES_FILTER	=222;


	final static String pageNumberPrefix;

	final static float PRICERATIO = 1.1;	//katalogus ar-szorzo
	
	int old_mem_texture_max;
	int old_mem_vertex_max;

	Player			player;

	Osd				osd;

	int				actGroup;

	Text			moneytxt, pgNumberL, pgNumberR;
	Text[]			pageNumberL = new Text[7];
	Text[]			pageNumberR = new Text[7];

	CatalogInventory	inventory;
	Vector			collector;
	GameRef			parts;

	int				curpage=1;

	int				step, filter, currentCarPackId, partsPackId;

	Decal[]			curDecals;
	Button[]		decalButtons;
	Vector			decals;
	int				showDecals;

	int				mainGroup, VehicleGroup, main1Group, main2Group, main3Group, main4Group, main5Group, main6Group, main1_1Group, main1_2Group, main1_3Group, main1_4Group, main1_5Group, main1_6Group, main1_7Group;

	int				decalsGroup, parts1Group, parts2Group, parts3Group, parts4Group, parts5Group, engine1Group, engine2Group, engine3Group, engine4Group, engine5Group, engine6Group, engine7Group;

	ResourceRef[]	carTypes;
	Button			VColor_Button, VehButton0, VehButton1, VehButton2, VehButton3, VehButton4, VehButton5, VehButton6, VehButton7, VehButton8, VehButton9, UpButton, DownButton;
	int				Rim_size, Tire_size, VehAmount, ColorIDX, CurrentPage, CurrentName, CompleteCar, RetailP, DeliveryP, TotalP, ButtonNum;
	Text			CurrentPageText;
	GameRef			CarScene, Cam;
	//Camera		Cam;
	RenderRef		SceneSun;
	Thread			CamThread;
	Ypr				CamYpr;
	Vector3			CamVector;
	Viewport		CarViewport;
	Vehicle			CurrentCar;
	GameRef			CurrentChassis;
	Text			CurrentCarName, RetailText, DeliveryText, TotalText;
	TextBox			CurrentCarDescription;
	Vector3			Size;

	public Catalog()
	{
		createNativeInstance();

		player = GameLogic.player;

		carTypes = GameLogic.VEHICLETYPE_ROOT.getChildNodes();
	}

	public static Vector collectDecals( String subdir )
	{
		Vector ds = new Vector();

		FindFile ff = new FindFile();
		String name=ff.first( "decals/textures/catalog/" + subdir + "/*.png" );
		while( name )
		{
			Decal decal = new Decal( "decals/textures/catalog/" +subdir + "/" + name );	//extends ResourceRef
			ds.addElement( decal );
			name = ff.next();
		}
		ff.close();

		return ds;
	}

	public void clearObjectCache()
	{
		if( inventory )
		{
			inventory.hide();
			inventory.flushAll();
			inventory = null;
		}

		collector = null;
	}

	//puts parts of the given category and car type (+common parts) to the given inventory
	public void collectObjectsBegin()
	{
		clearObjectCache();

		inventory = new CatalogInventory( this, player, 0.02, 0.25, 0.96, 0.50 );
		collector = new Vector();

		if( player.car )
			currentCarPackId = player.car.getInfo( GII_TYPE ) >> 16;
		else
			currentCarPackId = 0;

		partsPackId = System.openLib( "parts.rpk" );
	}

	public void collectObjectsEnd()
	{
		collectObjectsStep(9);

		inventory.update();
		inventory.show();
	}
	
	public void collectObjects( GameRef root )
	{
		collectObjects( root, null, null, null, null);
	}

	public void collectObjects( GameRef root1, GameRef root2 )
	{
		collectObjects( root1, root2, null, null, null);
	}

	public void collectObjects( GameRef root1, GameRef root2, GameRef root3 )
	{
		collectObjects( root1, root2, root3, null, null);
	}

	public void collectObjects( GameRef root1, GameRef root2, GameRef root3, GameRef root4 )
	{
		collectObjects( root1, root2, root3, root4, null);
	}

	public void collectObjects( GameRef root1, GameRef root2, GameRef root3, GameRef root4, GameRef root5 )
	{
		if( root1 ) collector.addElement( root1 );
		if( root2 ) collector.addElement( root2 );
		if( root3 ) collector.addElement( root3 );
		if( root4 ) collector.addElement( root4 );
		if( root5 ) collector.addElement( root5 );
	}

	//return false if finished collecting
	public int collectObjectsStep( int int_step )
	{
		step = int_step;

		if( collector )
		{
			while( !collector.isEmpty() && step )
			{
				parts=collector.removeLastElement();

				if( parts.isScripted( "java.game.parts.Part" ) )//it's a part
				{
					//inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
					//step--;
					osdCommand( filter );// run parts filter
				}
				else
				if( parts.isScripted( "java.game.parts.Set" ) )	//set?
				{
					Set set = parts.create( null, parts, null, "set_loader" );

					InventoryItem_Folder tmp = new InventoryItem_Folder( inventory );
					tmp.set = set;
					set.build( tmp.inv );
					inventory.items.addElement( tmp ); 
				}
				else
				{	//dummy eloszto node!
					if( parts=parts.getFirstChild() )
					{
						while( parts )
						{
							if( parts.isScripted() )
								collector.addElement( parts );
							else
							{
								int	carPack;
								int packId = parts.id() >> 16;
	
								for( int i=carTypes.length-1; i>=0; i-- )
								{
									if( packId == carTypes[i].id()>>16 )
									{
										carPack=1;
										break;
									}
								}
								
								if( !carPack || packId == currentCarPackId )
									collector.addElement( parts );
								//
							}

							parts=parts.getNextChild();
						}
					}
				}
			}

			inventory.update();

			return !collector.isEmpty();
		}

		return 0;
	}

	public void setDecalButtons()
	{
		int	max = decals.size();

		clearDecalButtons();

		for( int i=0; i<decalButtons.length; i++ )
		{
			//a gomb tipus (nagy)
			RenderRef	base = new RenderRef( frontend:0x00AEr );
	
			if( max > showDecals+i )
			{
				curDecals[i] = decals.elementAt( showDecals+i );
				decalButtons[i].rect.changeTexture( decals.elementAt( showDecals+i ) );
			}
		}
	}

	public void clearDecalButtons()
	{
		for( int i=0; i<decalButtons.length; i++ )
		{
			decalButtons[i].rect.changeTexture( Osd.RRT_EMPTY );
			if( curDecals[i] )
			{
				curDecals[i].unload();
				curDecals[i] = null;
			}
		}
	}

	public void enter( GameState prev_state )
	{
		osd = new Osd();
		osd.globalHandler = this;

		showDecals=0;

		setEventMask( EVENT_TIME );

		createOSDObjects();
		osd.show();

		Input.cursor.enable(1);
		setEventMask( EVENT_CURSOR );

		GfxEngine.setGlobalEnvmap( new ResourceRef( MW_Mod:0x0069r ));
		
		// should fix crashes by pinning memory limits to 0
		//RawEdit.engine_help_ForceMinAllMemLimits();
		//old_mem_vertex_max = RawEdit.GetIntFromRawAddress(RawEdit.Address_I_cfg_mem_vertex_max);
		//old_mem_texture_max = RawEdit.GetIntFromRawAddress(RawEdit.Address_I_cfg_mem_texture_max);
		//RawEdit.SetIntOnRawAddress(RawEdit.Address_I_cfg_mem_texture_max, 0);
		//RawEdit.SetIntOnRawAddress(RawEdit.Address_I_cfg_mem_vertex_max, 0);
	}

	public void exit( GameState next_state )
	{
		Frontend.loadingScreen.hide();

		clearEventMask( EVENT_ANY );
		removeAllTimers();
		Input.cursor.enable(0);

		pageNumberPrefix=null;
		
		// undoes the memory edits
		//RawEdit.engine_help_UnForceMinAllMemLimits();
		//RawEdit.SetIntOnRawAddress(RawEdit.Address_I_cfg_mem_texture_max, old_mem_texture_max);
		//RawEdit.SetIntOnRawAddress(RawEdit.Address_I_cfg_mem_vertex_max, old_mem_vertex_max);

		osd.hide();
	}

	public void VehicleScene()
	{
		CarScene = new Dummy( WORLDTREEROOT );
		
		CarViewport = new Viewport( 12, 0.53, 0.205, 0.45, 0.40 );
		CarViewport.activate( Viewport.RENDERFLAG_CLEARDEPTH | Viewport.RENDERFLAG_CLEARTARGET );
		//Cam = new Camera( CarScene, CarViewport, 0 );
		Cam = new GameRef( CarScene, GameRef.RID_CAMERA, "-2,1,-3 , -2.5,0,0 ,0x02", "camera" );
		Cam.setMatrix( new Vector3( -2, 1, -4), new Ypr( -2.7, -0.2, 0));
		Cam.command( "render " + CarViewport.id() + " 0 0 1 " + (Viewport.RENDERFLAG_CLEARDEPTH | Viewport.RENDERFLAG_CLEARTARGET) );
		Cam.command( "look " + CarScene.id() + " 0.3,-0.6,0" );
		Cam.command( "zoom 35 10");
		
		CurrentCar = new Vehicle();
			
		RenderRef CarSun = new RenderRef(misc.garage:0x001Dr);
		SceneSun = new RenderRef( CarScene, CarSun, "light for this scene" );

	// Reset at start
		ColorIDX = 0;
		CurrentPage = 0;
		CompleteCar = 0;

		osdCommand( CMD_UP ); // Refresh page
		osdCommand( CMD_VEH0 );// Show 1st car

		//getInfo( GameType.GII_SIZE )/100.0, 1 );	//size, enableRotate
		
		//CamThread = new Thread( this, "Cam Animate Refresher" );
		//CamThread.start();
	}

	public void createOSDObjects()
	{
		Style bsm = new Style( 0.70, 0.13, Frontend.largeFont, Text.ALIGN_RIGHT, Osd.RRT_TEST );
		Style bs = new Style( 0.70, 0.13, Frontend.mediumFont, Text.ALIGN_RIGHT, Osd.RRT_TEST );
		
		Style bbsr = new Style( 0.12, 0.12, 1.0, Frontend.mediumFont, Text.ALIGN_RIGHT, new ResourceRef(Osd.RID_ARROWRG) );
		Style bbsl = new Style( 0.12, 0.12, 1.0, Frontend.mediumFont, Text.ALIGN_LEFT, new ResourceRef(Osd.RID_ARROWLF) );

		Style bex = new Style( 0.12, 0.12, 1.0, Frontend.mediumFont, Text.ALIGN_CENTER, new ResourceRef(Osd.RID_EXIT) );
		Style bek = new Style( 0.12, 0.12, 1.0, Frontend.mediumFont, Text.ALIGN_CENTER, new ResourceRef(Osd.RID_BACK) );

		Style Menu_Left = new Style( 0.7, 0.13, Frontend.mediumFont, Text.ALIGN_LEFT, Osd.RRT_TEST );
		Style Menu_Right = new Style( 0.7, 0.13, Frontend.mediumFont, Text.ALIGN_RIGHT, Osd.RRT_TEST );
		Style Menu_Center = new Style( 0.4, 0.13, Frontend.mediumFont, Text.ALIGN_CENTER, Osd.RRT_TEST );
		Style Menu_Button = new Style( 0.12, 0.12, Frontend.mediumFont, Text.ALIGN_CENTER, null );
		Style Midle_Button = new Style( 0.17, 0.17, Frontend.mediumFont, Text.ALIGN_CENTER, null );
		Style Big_Button = new Style( 0.2, 0.25, Frontend.mediumFont, Text.ALIGN_CENTER, new ResourceRef( frontend:0x0125r ) );

		Menu m;

		//always visible objects:
		moneytxt=osd.createText( null, Frontend.mediumFont, Text.ALIGN_CENTER,	0.50, -0.95 );
		osd.endGroup();

		//----------------------------------------Grand Index

		osd.createBG( new ResourceRef(misc.catalog:0x0006r) );

		m= osd.createMenu( bsm, 1.0, -0.55, 0.18 );
		m.addItem( "VEHICLE", CMD_VEHICLE );
		m.addItem( "ENGINE", CMD_MAIN1 );
		m.addItem( "BODY", CMD_MAIN2 );
		m.addItem( "RUNNING GEAR", CMD_MAIN3 );
		m.addItem( "INTERIOR", CMD_MAIN4 );
		m.addItem( "AUDIO", CMD_MAIN5 );
		m.addItem( "DECALS", CMD_MAIN6 );
		
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_EXIT, this );
		actGroup = mainGroup = osd.endGroup();

		//----------------------------------------Engine Index

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Right, 0.98, -0.55, 0.13 );
		m.addItem( "I4 & ROTARY ENGINE", CMD_MAIN1_1 );
		m.addItem( "I5 & I6 ENGINE", CMD_MAIN1_2 );
		m.addItem( "V6 ENGINE", CMD_MAIN1_3 );
		m.addItem( "V8 ENGINE", CMD_MAIN1_4 );
		m.addItem( "V10 ENGINE", CMD_MAIN1_5 );
		m.addItem( "V12 ENGINE", CMD_MAIN1_6 );
		m.addItem( "UNSORTED & V16 ENGINE", CMD_MAIN1_7 );
		m.addSeparator();
		m.addSeparator();
		m.addItem( "ENGINE KITS", CMD_MAIN1_8 );
		
		osd.createButton( bek, 0.08, -0.92, CMD_FRONTPAGE, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_FRONTPAGE, this );
		osd.hideGroup( main1Group = osd.endGroup() );

		//----------------------------------------I4 Page

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.13 );
		m.addItem( "ENGINE BLOCKS", CMD_MAIN1_1_1 );
		m.addItem( "CRANKSHAFTS", CMD_MAIN1_1_2 );
		m.addItem( "CONNECTING RODS", CMD_MAIN1_1_3 );
		m.addItem( "PISTONS", CMD_MAIN1_1_4 );
		m.addItem( "TRANSMISSIONS", CMD_MAIN1_1_11 );
		m.addItem( "CLUTCH AND FLYWHEEL", CMD_MAIN1_1_12 );
		m.addItem( "MISCELLANEOUS", CMD_MAIN1_1_13 );
		
		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.13 );
		m.addItem( "CYLINDER HEADS AND COVERS", CMD_MAIN1_1_5 );
		m.addItem( "CAMSHAFTS", CMD_MAIN1_1_6 );
		m.addItem( "EXHAUST HEADERS", CMD_MAIN1_1_7 );
		m.addItem( "INTAKE HEADERS", CMD_MAIN1_1_8 );
		m.addItem( "FUEL SYSTEMS", CMD_MAIN1_1_9 );
		m.addItem( "PERFORMANCE SYSTEMS", CMD_MAIN1_1_10 );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( main1_1Group = osd.endGroup() );

		//----------------------------------------I6 Page

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.13 );
		m.addItem( "ENGINE BLOCKS", CMD_MAIN1_2_1 );
		m.addItem( "CRANKSHAFTS", CMD_MAIN1_2_2 );
		m.addItem( "CONNECTING RODS", CMD_MAIN1_2_3 );
		m.addItem( "PISTONS", CMD_MAIN1_2_4 );
		m.addItem( "TRANSMISSIONS", CMD_MAIN1_2_11 );
		m.addItem( "CLUTCH AND FLYWHEEL", CMD_MAIN1_2_12 );
		m.addItem( "MISCELLANEOUS", CMD_MAIN1_2_13 );
		
		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.13 );
		m.addItem( "CYLINDER HEADS AND COVERS", CMD_MAIN1_2_5 );
		m.addItem( "CAMSHAFTS", CMD_MAIN1_2_6 );
		m.addItem( "EXHAUST HEADERS", CMD_MAIN1_2_7 );
		m.addItem( "INTAKE HEADERS", CMD_MAIN1_2_8 );
		m.addItem( "FUEL SYSTEMS", CMD_MAIN1_2_9 );
		m.addItem( "PERFORMANCE SYSTEMS", CMD_MAIN1_2_10 );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( main1_2Group = osd.endGroup() );

		//----------------------------------------V6 Page

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.13 );
		m.addItem( "ENGINE BLOCKS", CMD_MAIN1_3_1 );
		m.addItem( "CRANKSHAFTS", CMD_MAIN1_3_2 );
		m.addItem( "CONNECTING RODS", CMD_MAIN1_3_3 );
		m.addItem( "PISTONS", CMD_MAIN1_3_4 );
		m.addItem( "TRANSMISSIONS", CMD_MAIN1_3_11 );
		m.addItem( "CLUTCH AND FLYWHEEL", CMD_MAIN1_3_12 );
		m.addItem( "MISCELLANEOUS", CMD_MAIN1_3_13 );
		
		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.13 );
		m.addItem( "CYLINDER HEADS AND COVERS", CMD_MAIN1_3_5 );
		m.addItem( "CAMSHAFTS", CMD_MAIN1_3_6 );
		m.addItem( "EXHAUST HEADERS", CMD_MAIN1_3_7 );
		m.addItem( "INTAKE HEADERS", CMD_MAIN1_3_8 );
		m.addItem( "FUEL SYSTEMS", CMD_MAIN1_3_9 );
		m.addItem( "PERFORMANCE SYSTEMS", CMD_MAIN1_3_10 );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( main1_3Group = osd.endGroup() );

		//----------------------------------------V8 Page

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.13 );
		m.addItem( "ENGINE BLOCKS", CMD_MAIN1_4_1 );
		m.addItem( "CRANKSHAFTS", CMD_MAIN1_4_2 );
		m.addItem( "CONNECTING RODS", CMD_MAIN1_4_3 );
		m.addItem( "PISTONS", CMD_MAIN1_4_4 );
		m.addItem( "TRANSMISSIONS", CMD_MAIN1_4_11 );
		m.addItem( "CLUTCH AND FLYWHEEL", CMD_MAIN1_4_12 );
		m.addItem( "MISCELLANEOUS", CMD_MAIN1_4_13 );
		
		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.13 );
		m.addItem( "CYLINDER HEADS AND COVERS", CMD_MAIN1_4_5 );
		m.addItem( "CAMSHAFTS", CMD_MAIN1_4_6 );
		m.addItem( "EXHAUST HEADERS", CMD_MAIN1_4_7 );
		m.addItem( "INTAKE HEADERS", CMD_MAIN1_4_8 );
		m.addItem( "FUEL SYSTEMS", CMD_MAIN1_4_9 );
		m.addItem( "PERFORMANCE SYSTEMS", CMD_MAIN1_4_10 );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( main1_4Group = osd.endGroup() );

		//----------------------------------------V10 Page

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.13 );
		m.addItem( "ENGINE BLOCKS", CMD_MAIN1_5_1 );
		m.addItem( "CRANKSHAFTS", CMD_MAIN1_5_2 );
		m.addItem( "CONNECTING RODS", CMD_MAIN1_5_3 );
		m.addItem( "PISTONS", CMD_MAIN1_5_4 );
		m.addItem( "TRANSMISSIONS", CMD_MAIN1_5_11 );
		m.addItem( "CLUTCH AND FLYWHEEL", CMD_MAIN1_5_12 );
		m.addItem( "MISCELLANEOUS", CMD_MAIN1_5_13 );
		
		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.13 );
		m.addItem( "CYLINDER HEADS AND COVERS", CMD_MAIN1_5_5 );
		m.addItem( "CAMSHAFTS", CMD_MAIN1_5_6 );
		m.addItem( "EXHAUST HEADERS", CMD_MAIN1_5_7 );
		m.addItem( "INTAKE HEADERS", CMD_MAIN1_5_8 );
		m.addItem( "FUEL SYSTEMS", CMD_MAIN1_5_9 );
		m.addItem( "PERFORMANCE SYSTEMS", CMD_MAIN1_5_10 );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( main1_5Group = osd.endGroup() );

		//----------------------------------------V12 Page

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.13 );
		m.addItem( "ENGINE BLOCKS", CMD_MAIN1_6_1 );
		m.addItem( "CRANKSHAFTS", CMD_MAIN1_6_2 );
		m.addItem( "CONNECTING RODS", CMD_MAIN1_6_3 );
		m.addItem( "PISTONS", CMD_MAIN1_6_4 );
		m.addItem( "TRANSMISSIONS", CMD_MAIN1_6_11 );
		m.addItem( "CLUTCH AND FLYWHEEL", CMD_MAIN1_6_12 );
		m.addItem( "MISCELLANEOUS", CMD_MAIN1_6_13 );
		
		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.13 );
		m.addItem( "CYLINDER HEADS AND COVERS", CMD_MAIN1_6_5 );
		m.addItem( "CAMSHAFTS", CMD_MAIN1_6_6 );
		m.addItem( "EXHAUST HEADERS", CMD_MAIN1_6_7 );
		m.addItem( "INTAKE HEADERS", CMD_MAIN1_6_8 );
		m.addItem( "FUEL SYSTEMS", CMD_MAIN1_6_9 );
		m.addItem( "PERFORMANCE SYSTEMS", CMD_MAIN1_6_10 );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( main1_6Group = osd.endGroup() );

		//----------------------------------------V16 Page

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.13 );
		m.addItem( "ENGINE BLOCKS", CMD_MAIN1_7_1 );
		m.addItem( "CRANKSHAFTS", CMD_MAIN1_7_2 );
		m.addItem( "CONNECTING RODS", CMD_MAIN1_7_3 );
		m.addItem( "PISTONS", CMD_MAIN1_7_4 );
		m.addItem( "TRANSMISSIONS", CMD_MAIN1_7_11 );
		m.addItem( "CLUTCH AND FLYWHEEL", CMD_MAIN1_7_12 );
		m.addItem( "MISCELLANEOUS", CMD_MAIN1_7_13 );
		
		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.13 );
		m.addItem( "CYLINDER HEADS AND COVERS", CMD_MAIN1_7_5 );
		m.addItem( "CAMSHAFTS", CMD_MAIN1_7_6 );
		m.addItem( "EXHAUST HEADERS", CMD_MAIN1_7_7 );
		m.addItem( "INTAKE HEADERS", CMD_MAIN1_7_8 );
		m.addItem( "FUEL SYSTEMS", CMD_MAIN1_7_9 );
		m.addItem( "PERFORMANCE SYSTEMS", CMD_MAIN1_7_10 );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( main1_7Group = osd.endGroup() );

		//----------------------------------------Vehicle Page

		osd.createBG( new ResourceRef(MW_Mod:0x0068r) ); // Background

		CurrentName = CurrentPage*10;

		m = osd.createMenu( Menu_Center, 0.8, -0.775, Osd.MD_HORIZONTAL ); 
		m.addItem( "VEHICLE", CMD_VEHI );

		m = osd.createMenu( Menu_Center, 0.45, -0.775, Osd.MD_HORIZONTAL );
		m.addItem( "CHASSIS", CMD_CHAS );

		m = osd.createMenu( Midle_Button, 0.9, 0.1, 0, Osd.MD_VERTICAL );
		VColor_Button = m.addItem( new ResourceRef( frontend:0x00AFr ), CMD_VCOLOR, "Change color" );
		VColor_Button.disable();

		CurrentPageText = osd.createText( "Current Page: " + (CurrentPage+1), Frontend.largeFont, Text.ALIGN_CENTER, -0.45, -0.86 ); CurrentPageText.changeColor(0xFFC0C0C0);// "Unselected" gray
		CurrentCarName = osd.createText( "-NO VEHICLE SELECTED-", Frontend.largeFont, Text.ALIGN_CENTER, 0.50, -0.65 ); CurrentCarName.changeColor(0xFFC0C0C0);// "Unselected" gray

		CurrentCarDescription = osd.createTextBox( "Description: N/A", Frontend.smallFont, Text.ALIGN_CENTER, 0.09, 0.21, 0.85, null, 10 ); CurrentCarDescription.changeColor(0xFFC0C0C0);// text box, 10 lines max

		osd.createText( "Retail Price:", Frontend.smallFont, Text.ALIGN_LEFT, 0.12, 0.77 ).changeColor(0xFFC0C0C0);// "Unselected" gray
		RetailText = osd.createText( "N/A", Frontend.smallFont, Text.ALIGN_RIGHT, 0.65, 0.77 ); RetailText.changeColor(0xFFC0C0C0);// "Unselected" gray

		osd.createText( "Delivery Cost:", Frontend.smallFont, Text.ALIGN_LEFT, 0.12, 0.82 ).changeColor(0xFFC0C0C0);// "Unselected" gray
		DeliveryText = osd.createText( "N/A", Frontend.smallFont, Text.ALIGN_RIGHT, 0.65, 0.82 ); DeliveryText.changeColor(0xFFC0C0C0);// "Unselected" gray

		osd.createText( "Total:", Frontend.smallFont, Text.ALIGN_LEFT, 0.12, 0.88 ).changeColor(0xFFC0C0C0);// "Unselected" gray
		TotalText = osd.createText( "N/A", Frontend.smallFont, Text.ALIGN_RIGHT, 0.65, 0.88 ); TotalText.changeColor(0xFFC0C0C0);// "Unselected" gray

		//osd.createText( "What you want to have today?", Frontend.smallFont, Text.ALIGN_CENTER, -0.45, 0.80 ).changeColor(0xFFC0C0C0);// "Unselected" gray
		osd.createText( "Choose between " + GameLogic.ChassisBox.size() + " different models." , Frontend.smallFont, Text.ALIGN_CENTER, -0.45, 0.85 ).changeColor(0xFFC0C0C0);// "Unselected" gray

		VehAmount = GameLogic.ChassisBox.size()-1;

		m= osd.createMenu( Menu_Left, -0.98, -0.60, 0 );
		if( CurrentName + 0 <= VehAmount ) VehButton0 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+0].vehicleName, CMD_VEH0 ); else { VehButton0 = m.addItem( "", CMD_VEH0 ); VehButton0.disable(); }
		if( CurrentName + 1 <= VehAmount ) VehButton1 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+1].vehicleName, CMD_VEH1 ); else { VehButton1 = m.addItem( "", CMD_VEH1 ); VehButton0.disable(); }
		if( CurrentName + 2 <= VehAmount ) VehButton2 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+2].vehicleName, CMD_VEH2 ); else { VehButton2 = m.addItem( "", CMD_VEH2 ); VehButton0.disable(); }
		if( CurrentName + 3 <= VehAmount ) VehButton3 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+3].vehicleName, CMD_VEH3 ); else { VehButton3 = m.addItem( "", CMD_VEH3 ); VehButton0.disable(); }
		if( CurrentName + 4 <= VehAmount ) VehButton4 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+4].vehicleName, CMD_VEH4 ); else { VehButton4 = m.addItem( "", CMD_VEH4 ); VehButton0.disable(); }
		if( CurrentName + 5 <= VehAmount ) VehButton5 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+5].vehicleName, CMD_VEH5 ); else { VehButton5 = m.addItem( "", CMD_VEH5 ); VehButton0.disable(); }
		if( CurrentName + 6 <= VehAmount ) VehButton6 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+6].vehicleName, CMD_VEH6 ); else { VehButton6 = m.addItem( "", CMD_VEH6 ); VehButton0.disable(); }
		if( CurrentName + 7 <= VehAmount ) VehButton7 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+7].vehicleName, CMD_VEH7 ); else { VehButton7 = m.addItem( "", CMD_VEH7 ); VehButton0.disable(); }
		if( CurrentName + 8 <= VehAmount ) VehButton8 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+8].vehicleName, CMD_VEH8 ); else { VehButton8 = m.addItem( "", CMD_VEH8 ); VehButton0.disable(); }
		if( CurrentName + 9 <= VehAmount ) VehButton9 = m.addItem(GameLogic.ChassisBox.elementData[CurrentName+9].vehicleName, CMD_VEH9 ); else { VehButton9 = m.addItem( "", CMD_VEH9 ); VehButton0.disable(); }

		m = osd.createMenu( Menu_Button, -0.90,  -0.82, Osd.MD_VERTICAL ); 
		UpButton = m.addItem( new ResourceRef( frontend:0x00AFr ), CMD_UP, "Prev Page" );
		
		m = osd.createMenu( Menu_Button, -0.90,  0.82, Osd.MD_VERTICAL );
		DownButton = m.addItem( new ResourceRef( frontend:0x0084r ), CMD_DOWN, "Next Page" );
		
		osd.createButton( bek, 0.08, -0.92, CMD_FRONTPAGE, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createButton( Big_Button, 0.88, 0.847, CMD_VBUY, "Buy this vehicle" );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_FRONTPAGE, this );
		osd.hideGroup( VehicleGroup = osd.endGroup() );

		//----------------------------------------Running Gear Index

		osd.createBG( new ResourceRef(misc.catalog:0x0010r) );

		m= osd.createMenu( bs, 1.0, -0.55, 0.18 );
		m.addItem( "BODY PANELS", CMD_MAIN2_1 );
		m.addItem( "REPLACEMENT PARTS", CMD_MAIN2_2 );
		m.addItem( "LIGHTS & WINDOWS", CMD_MAIN2_3 );
		m.addItem( "NEON LIGHTS & MISC", CMD_MAIN2_4 );
		m.addItem( "AERODYNAMIC TUNING", CMD_MAIN2_5 );
		m.addItem( "MUFFLERS", CMD_MAIN2_6 );
		m.addItem( "BODY KITS", CMD_MAIN2_7 );
		
		osd.createButton( bek, 0.08, -0.92, CMD_FRONTPAGE, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_FRONTPAGE, this );
		osd.hideGroup( main2Group = osd.endGroup() );

		//----------------------------------------Running Gear Index

		osd.createBG( new ResourceRef(misc.catalog:0x0011r) );

		m= osd.createMenu( Menu_Right, 0.98, -0.6, 0.15 );
		m.addItem( "SUSPENSIONS", CMD_MAIN3_1 );
		m.addItem( "SHOCK ABSORBERS", CMD_MAIN3_2 );
		m.addItem( "SPRINGS", CMD_MAIN3_3 );
		m.addItem( "BRAKE SYSTEMS", CMD_MAIN3_4 );
		m.addSeparator();
		m.addItem( "OTHERS", CMD_MAIN3_6 );
		m.addItem( "PRO TUNING", CMD_MAIN3_7 );
		m.addItem( "RUNNING GEAR KITS", CMD_MAIN3_8 );

		m= osd.createMenu( Menu_Left, -0.98, -0.6, 0.133 );
		m.addItem( "SMALLER RIMS", CMD_MAIN3_9 );
		m.addItem( "RIMS 15 INCH", CMD_MAIN3_10 );
		m.addItem( "RIMS 16 INCH", CMD_MAIN3_11 );
		m.addItem( "RIMS 17 INCH", CMD_MAIN3_12 );
		m.addItem( "RIMS 18 INCH", CMD_MAIN3_13 );
		m.addItem( "RIMS 19 INCH", CMD_MAIN3_14 );
		m.addItem( "RIMS 20 INCH", CMD_MAIN3_15 );
		m.addItem( "RIMS 21 INCH", CMD_MAIN3_16 );
		m.addItem( "RIMS 22 INCH", CMD_MAIN3_17 );
		m.addItem( "BIGGER RIMS ", CMD_MAIN3_18 );
		
		m= osd.createMenu( Menu_Left, -0.34, -0.6, 0.133 );
		m.addItem( "SMALLER TIRES", CMD_MAIN3_19 );
		m.addItem( "TIRES 15 INCH", CMD_MAIN3_20 );
		m.addItem( "TIRES 16 INCH", CMD_MAIN3_21 );
		m.addItem( "TIRES 17 INCH", CMD_MAIN3_22 );
		m.addItem( "TIRES 18 INCH", CMD_MAIN3_23 );
		m.addItem( "TIRES 19 INCH", CMD_MAIN3_24 );
		m.addItem( "TIRES 20 INCH", CMD_MAIN3_25 );
		m.addItem( "TIRES 21 INCH", CMD_MAIN3_26 );
		m.addItem( "TIRES 22 INCH", CMD_MAIN3_27 );
		m.addItem( "BIGGER TIRES ", CMD_MAIN3_28 );
		
		osd.createButton( bek, 0.08, -0.92, CMD_FRONTPAGE, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_FRONTPAGE, this );
		osd.hideGroup( main3Group = osd.endGroup() );

		//----------------------------------------Interior Index

		osd.createBG( new ResourceRef(misc.catalog:0x001Er) );

		m= osd.createMenu( bs, 1.0, -0.55, 0 );
		m.addItem( "STEERING WHEELS", CMD_MAIN4_1 );
		m.addItem( "DASHES", CMD_MAIN4_2 );
		m.addItem( "GEAR KNOBS", CMD_MAIN4_3 );
		m.addItem( "PEDALS", CMD_MAIN4_4 );
		m.addItem( "GAUGES", CMD_MAIN4_5 );
		m.addItem( "SEATS", CMD_MAIN4_6 );
		m.addItem( "ROLLBARS", CMD_MAIN4_7 );
		m.addItem( "DECORATION", CMD_MAIN4_8 );
		
		osd.createButton( bek, 0.08, -0.92, CMD_FRONTPAGE, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_FRONTPAGE, this );
		osd.hideGroup( main4Group = osd.endGroup() );

		//----------------------------------------Audio Index

		osd.createBG( new ResourceRef(misc.catalog:0x001Fr) );

		m= osd.createMenu( bs, 1.0, -0.55, 0.2 );
		m.addItem( "HEAD UNITS", CMD_MAIN5_1 );
		m.addItem( "AMPLIFIERS", CMD_MAIN5_2 );
		m.addItem( "BOXES", CMD_MAIN5_3 );
		m.addItem( "SUBWOOFERS", CMD_MAIN5_4 );
		m.addItem( "ACCESSORIES", CMD_MAIN5_5 );
		m.addItem( "AUDIO KITS", CMD_MAIN5_6 );
		
		osd.createButton( bek, 0.08, -0.92, CMD_FRONTPAGE, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_FRONTPAGE, this );
		osd.hideGroup( main5Group = osd.endGroup() );

		//----------------------------------------Decal Index

		osd.createBG( new ResourceRef(misc.catalog:0x0022r) );

		m= osd.createMenu( bs, 1.0, -0.55, 0.18 );
		m.addItem( "MANUFACTURERS", CMD_MAIN6_1 );
		m.addItem( "LOGOS", CMD_MAIN6_2 );
		m.addItem( "NUMBERS", CMD_MAIN6_3 );
		m.addItem( "DIGITS", CMD_MAIN6_4 );
		m.addItem( "SMILIES", CMD_MAIN6_5 );
		m.addItem( "ANIMALS", CMD_MAIN6_6 );
		m.addItem( "MISC", CMD_MAIN6_7 );
		
		osd.createButton( bek, 0.08, -0.92, CMD_FRONTPAGE, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_FRONTPAGE, this );
		osd.hideGroup( main6Group = osd.endGroup() );

		//----------------------------------------Engine parts main catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1, this );
		osd.hideGroup( parts1Group = osd.endGroup() );

		//----------------------------------------I4 parts catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1_1, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1_1, this );
		osd.hideGroup( engine1Group = osd.endGroup() );

		//----------------------------------------I6 parts catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1_2, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1_2, this );
		osd.hideGroup( engine2Group = osd.endGroup() );


		//----------------------------------------V6 parts catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1_3, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1_3, this );
		osd.hideGroup( engine3Group = osd.endGroup() );

		//----------------------------------------V8 parts catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1_4, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1_4, this );
		osd.hideGroup( engine4Group = osd.endGroup() );

		//----------------------------------------V10 parts catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1_5, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1_5, this );
		osd.hideGroup( engine5Group = osd.endGroup() );

		//----------------------------------------V12 parts catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1_6, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1_6, this );
		osd.hideGroup( engine6Group = osd.endGroup() );

		//----------------------------------------V16 parts catalog

		osd.createBG( new ResourceRef(misc.catalog:0x0009r) );

		pageNumberL[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[0] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN1_7, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN1_7, this );
		osd.hideGroup( engine7Group = osd.endGroup() );

		//----------------------------------------Akatreszlista oldalak 2
		osd.createBG( new ResourceRef(misc.catalog:0x0010r) );

		pageNumberL[1] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[1] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN2, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN2, this );
		osd.hideGroup( parts2Group = osd.endGroup() );

		//----------------------------------------Akatreszlista oldalak 3
		osd.createBG( new ResourceRef(misc.catalog:0x0011r) );

		pageNumberL[2] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[2] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN3, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN3, this );
		osd.hideGroup( parts3Group = osd.endGroup() );

		//----------------------------------------Akatreszlista oldalak 4
		osd.createBG( new ResourceRef(misc.catalog:0x001Er) );

		pageNumberL[3] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[3] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN4, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN4, this );
		osd.hideGroup( parts4Group = osd.endGroup() );

		//----------------------------------------Akatreszlista oldalak 5
		osd.createBG( new ResourceRef(misc.catalog:0x001Fr) );

		pageNumberL[4] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[4] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVPARTPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTPARTPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN5, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN5, this );
		osd.hideGroup( parts5Group = osd.endGroup() );

		//----------------------------------------Altalanos decallista oldal

		osd.createBG( new ResourceRef(misc.catalog:0x0022r) );

		Style dbs = new Style( 0.45, 0.45, Frontend.largeFont, Text.ALIGN_CENTER, Osd.RRT_TEST );

		curDecals = new Decal[8];
		decalButtons=new Button[8];
		decalButtons[0] = osd.createButton( dbs, -0.75, -0.35, "", CMD_DB0 );
		decalButtons[1] = osd.createButton( dbs, -0.25, -0.35, "", CMD_DB1 );
		decalButtons[2] = osd.createButton( dbs,  0.25, -0.35, "", CMD_DB2 );
		decalButtons[3] = osd.createButton( dbs,  0.75, -0.35, "", CMD_DB3 );
		decalButtons[4] = osd.createButton( dbs, -0.75,  0.40, "", CMD_DB4 );
		decalButtons[5] = osd.createButton( dbs, -0.25,  0.40, "", CMD_DB5 );
		decalButtons[6] = osd.createButton( dbs,  0.25,  0.40, "", CMD_DB6 );
		decalButtons[7] = osd.createButton( dbs,  0.75,  0.40, "", CMD_DB7 );

		pageNumberL[5] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_LEFT,	-0.96,  0.82);
		pageNumberR[5] = osd.createText( null, Frontend.mediumFont, Text.ALIGN_RIGHT,	0.96,  0.82);

		osd.createButton( bbsl, -0.96, 0.92, CMD_PREVDECALPAGE, null );
		osd.createButton( bbsr, 0.96, 0.92, CMD_NEXTDECALPAGE, null );

		osd.createButton( bek, 0.08, -0.92, CMD_MAIN6, null );
		osd.createButton( bex, 0.92, -0.92, CMD_EXIT, null );

		osd.createHotkey( Input.AXIS_CANCEL, Input.VIRTUAL, CMD_MAIN6, this );
		osd.hideGroup( decalsGroup = osd.endGroup() );

		//------------------------------------------

		refreshMoneyString();
		refreshPage();
	}	


	public void changeGroup(int group)
	{
		if( actGroup != group )
		{
			if (actGroup >= 0)
			{
				osd.hideGroup (actGroup);

				//group deinit code:
				if( actGroup == decalsGroup )
				{
					clearDecalButtons();
				}
				else
				if( actGroup >= parts1Group && actGroup <= parts5Group )
				{
					clearObjectCache();
				}
			}

			actGroup = group;

			if (actGroup >= 0)
			{
				osd.showGroup (actGroup);
				osd.changeSelection2( -1, 0 );
			}
		}
	}


	public void refreshMoneyString()
	{
		new SfxRef( Frontend.SFX_MONEY ).play(); 
		moneytxt.changeText( "$" + Integer.toString( player.money ) );
	}

	public void refreshPage()
	{
		if( pageNumberPrefix )
		{
			pgNumberL.changeText( pageNumberPrefix + curpage );
			pgNumberR.changeText( pageNumberPrefix + (curpage+1) );
		}
	}

//----------------------------------------------------------------------

	public void decalButtonPressed( int n )
	{
		if( showDecals+n < decals.size() )
		{
			int price=DECALPRICE;
			if( price <= player.money )
			{
				Decal decal = decals.elementAt( showDecals+n );

				Dialog dialog = new YesNoDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "BUY DECAL SET", "Do you want to buy this decal set for $" + price + " ?\n(Each set contains 5 decals)" );
				if( dialog.display() == 0 )
				{
					player.money-=price;
					player.decals.addElement( new Decal( decal.id() ) );
					refreshMoneyString();
				}
			}
			else
			{
				new WarningDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "NOT ENOUGHT MONEY", "You don't have $" + price + " to buy a decal!" ).display();
			}
		}
	}

//----------------------------------------------------------------------
	public void osdCommand( int cmd )
	{
		if( cmd == CMD_VCOLOR )
		{
			if(CurrentCar)
			{
				if( ColorIDX == GameLogic.CARCOLORS.length - 1) 
					ColorIDX = 0;
				else
					ColorIDX += 1;

				CurrentCar.command( "texture " + GameLogic.CARCOLORS[ColorIDX] + " 1" );
				VColor_Button.changeTexture( new ResourceRef(GameLogic.CARCOLORS[ColorIDX]) );
			}
		}
		if( cmd == CMD_VBUY )
		{
			if( TotalP <= player.money )
			{
			// Create name: car name or chassis name
				String Vname;
				if( CompleteCar > 0 ) Vname = CurrentCar.chassis.vehicleName;
				else Vname = CurrentCar.chassis.name;

			// Check for free space at garage
				int	GarageFree;

				if( GameLogic.player.car )
				{
					//GameLogic.garage.releaseCar();
					GameLogic.player.carlot.lockPlayerCar();
					GarageFree = GameLogic.player.carlot.getFreeSlot();
					GameLogic.player.carlot.releasePlayerCar();
					
				//Reset car pos at garage
					GameLogic.garage.StartPos = new Vector3(0, 0, 0);
					GameLogic.garage.StartYpr = new Ypr(0, 0, 0);
				}
				else
					GarageFree = 1;

				if( GarageFree >= 0 )
				{
					Dialog dialog = new NoYesDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "BUY CAR", "Do you want to buy this " + Vname  + " for $" + TotalP + " ?" );
					if( dialog.display() == 0 )
					{
						player.money-=TotalP;
						refreshMoneyString();

						player.carlot.addCar( player.car );
						player.carlot.saveCar( player.carlot.curcar );
						player.carlot.flushCars();

						//CurrentCar.command( "start" );	//release
						
						if (File.exists("save/temp/CopyCar"))
						{
							File.delete( "save/temp/CopyCar" );
							File.delete( "save/temp/CopyCar.*" );
						}
						CurrentCar.save( "save/temp/CopyCar" );

						if(CurrentCar) CurrentCar.destroy();
						if(CurrentChassis) CurrentChassis.destroy();

						GameLogic.player.car = Vehicle.load( "save/temp/CopyCar", GameLogic.player );

						changeGroup( -1 );
						GameLogic.changeActiveSection( GameLogic.garage );
					}
				}
				else 
				{ 
					new WarningDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "CAR LOT FULL", "There is no more free space in your car lot. \n Sell some cars to free up parking space!" ).display();
				}
			}
			else
			{
					new WarningDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "NOT ENOUGHT MONEY", "You don't have $" + TotalP + " to buy this car!" ).display();
			}
		}
		if( cmd == CMD_CHAS )
		{
			if(CompleteCar > 0)
			{
				if(CurrentCar) CurrentCar.destroy();
				if(CurrentChassis) CurrentChassis.destroy();

				CurrentCar = new Vehicle();
				CurrentCar.chassis = new Chassis();
				CurrentCar.chassis = create(CarScene, new GameRef(GameLogic.ChassisRoot[GameLogic.ChassisResNoumber[CurrentName+ButtonNum]]), "0,0,0,0,0,0,", "chassis");
				CurrentCar = new Vehicle(CurrentCar.chassis);

				CurrentCarName.changeText( CurrentCar.chassis.vehicleName );
				CurrentCarDescription.changeText( CurrentCar.chassis.description );
				
				RetailP = CurrentCar.getTotalPrice()*PRICERATIO; // Clac retail price
				RetailText.changeText( "$" + RetailP );// show text

				DeliveryP = CurrentCar.chassis.getMass()*5.8; // Calc delivery price
				DeliveryText.changeText( "$" + DeliveryP );// show text
				
				TotalP = RetailP + DeliveryP; // Calc total price
				TotalText.changeText( "$" + TotalP );// show text

				VColor_Button.disable();
				VColor_Button.changeTexture( new ResourceRef( frontend:0x00AFr ) );
			}

			CompleteCar = 0;
		}
		if( cmd == CMD_VEHI )
		{
			if(CompleteCar == 0 && CurrentCar)
			{
				CurrentCar.chassis.addStockParts( new Descriptor( CurrentCar.chassis.getTexture(), 1, 1, 1, 1) );
				CurrentCar.chassis.command( "reset" );
				CurrentCar.chassis.command( "setsteer -0.6" );

				RetailP = CurrentCar.getTotalPrice()*PRICERATIO; // Clac retail price
				RetailText.changeText( "$" + RetailP );// show text

				DeliveryP = CurrentCar.chassis.getMass()*5.8; // Calc delivery price
				DeliveryText.changeText( "$" + DeliveryP );// show text
				
				TotalP = RetailP + DeliveryP; // Calc total price
				TotalText.changeText( "$" + TotalP );// show text

				VColor_Button.enable();
				VColor_Button.changeTexture( new ResourceRef(CurrentCar.chassis.getTexture()) );
			}
			CompleteCar = 1;
		}
		if( cmd >= CMD_VEH0 && cmd <= CMD_VEH9 ) // All 10 buttons
		{
			if(cmd == CMD_VEH0) ButtonNum = 0;
			if(cmd == CMD_VEH1) ButtonNum = 1;
			if(cmd == CMD_VEH2) ButtonNum = 2;
			if(cmd == CMD_VEH3) ButtonNum = 3;
			if(cmd == CMD_VEH4) ButtonNum = 4;
			if(cmd == CMD_VEH5) ButtonNum = 5;
			if(cmd == CMD_VEH6) ButtonNum = 6;
			if(cmd == CMD_VEH7) ButtonNum = 7;
			if(cmd == CMD_VEH8) ButtonNum = 8;
			if(cmd == CMD_VEH9) ButtonNum = 9;

			if(CurrentCar) CurrentCar.destroy();
			if(CurrentChassis) CurrentChassis.destroy();

			CurrentCar = new Vehicle();
			CurrentCar.chassis = new Chassis();
			CurrentCar.chassis = create(CarScene, new GameRef(GameLogic.ChassisRoot[GameLogic.ChassisResNoumber[CurrentName+ButtonNum]]), "0,0,0,0,0,0,", "chassis");
			CurrentCar = new Vehicle(CurrentCar.chassis);

			Size = CurrentCar.chassis.getMin();
			Cam.command( "move " + CarScene.id() +" "+(Size.x*2.2)+","+(Size.y*2.2)+","+(Size.z*2.2)+" 1.0 0, -0.2, 0" );
			
			//Cam.setMatrix( new Vector3(-(Max.x-Min.x+1), Max.y, -(Max.z-Min.z+1)), new Ypr(-2.358, -0.5, 0));
			//float size = CurrentCar.chassis.getInfo(GameType.GII_SIZE)/100.0;

			if(CompleteCar)
			{
				CurrentCar.chassis.addStockParts( new Descriptor( CurrentCar.chassis.getTexture(), 1, 1, 1, 1) );
				CurrentCar.chassis.command( "reset" );
				CurrentCar.chassis.command( "setsteer -0.6" );

				VColor_Button.enable();
				VColor_Button.changeTexture( new ResourceRef(CurrentCar.chassis.getTexture()) );
			}

			CurrentCarName.changeText( CurrentCar.chassis.vehicleName );
			CurrentCarDescription.changeText( CurrentCar.chassis.description );
			
			RetailP = CurrentCar.getTotalPrice()*PRICERATIO; // Clac retail price
			RetailText.changeText( "$" + RetailP );// show text

			DeliveryP = CurrentCar.chassis.getMass()*5.8; // Calc delivery price
			DeliveryText.changeText( "$" + DeliveryP );// show text
			
			TotalP = RetailP + DeliveryP; // Calc total price
			TotalText.changeText( "$" + TotalP );// show text
		}
		else
		if( cmd == CMD_UP )
		{
			if (CurrentPage > 0)
				CurrentPage -= 1;

			CurrentName = CurrentPage*10;
			CurrentPageText.changeText( "Current Page: " + (CurrentPage+1) );

			VehAmount = GameLogic.ChassisBox.size()-1;

			DownButton.changeTexture( new ResourceRef( frontend:0x0084r ) ); // Arrow down
			DownButton.enable();
			
			if (CurrentPage == 0)
			{
				UpButton.changeTexture( new ResourceRef( frontend:0x00AFr ) ); // Empty.ptx
				UpButton.disable();
			}

			if( CurrentName + 0 <= VehAmount ) { VehButton0.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+0].vehicleName); VehButton0.enable(); } else { VehButton0.changeLabelText( "" ); VehButton0.disable(); }
			if( CurrentName + 1 <= VehAmount ) { VehButton1.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+1].vehicleName); VehButton1.enable(); } else { VehButton1.changeLabelText( "" ); VehButton1.disable(); }
			if( CurrentName + 2 <= VehAmount ) { VehButton2.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+2].vehicleName); VehButton2.enable(); } else { VehButton2.changeLabelText( "" ); VehButton2.disable(); }
			if( CurrentName + 3 <= VehAmount ) { VehButton3.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+3].vehicleName); VehButton3.enable(); } else { VehButton3.changeLabelText( "" ); VehButton3.disable(); }
			if( CurrentName + 4 <= VehAmount ) { VehButton4.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+4].vehicleName); VehButton4.enable(); } else { VehButton4.changeLabelText( "" ); VehButton4.disable(); }
			if( CurrentName + 5 <= VehAmount ) { VehButton5.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+5].vehicleName); VehButton5.enable(); } else { VehButton5.changeLabelText( "" ); VehButton5.disable(); }
			if( CurrentName + 6 <= VehAmount ) { VehButton6.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+6].vehicleName); VehButton6.enable(); } else { VehButton6.changeLabelText( "" ); VehButton6.disable(); }
			if( CurrentName + 7 <= VehAmount ) { VehButton7.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+7].vehicleName); VehButton7.enable(); } else { VehButton7.changeLabelText( "" ); VehButton7.disable(); }
			if( CurrentName + 8 <= VehAmount ) { VehButton8.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+8].vehicleName); VehButton8.enable(); } else { VehButton8.changeLabelText( "" ); VehButton8.disable(); }
			if( CurrentName + 9 <= VehAmount ) { VehButton9.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+9].vehicleName); VehButton9.enable(); } else { VehButton9.changeLabelText( "" ); VehButton9.disable(); }
		}
		else
		if( cmd == CMD_DOWN )
		{
			if (GameLogic.ChassisRoot.length >= CurrentName+10)
				CurrentPage += 1;

			CurrentName = CurrentPage*10;

			CurrentPageText.changeText( "Current Page: " + (CurrentPage+1) );

			UpButton.changeTexture( new ResourceRef(frontend:0x004Cr) ); // Arrow up
			UpButton.enable();

			VehAmount = GameLogic.ChassisBox.size()-1;

			if (VehAmount < CurrentName+10)
			{
				DownButton.changeTexture( new ResourceRef(frontend:0x00AFr) ); // Empty.ptx
				DownButton.disable();
			}

			if( CurrentName + 0 <= VehAmount ) { VehButton0.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+0].vehicleName); VehButton0.enable(); } else { VehButton0.changeLabelText( "" ); VehButton0.disable(); }
			if( CurrentName + 1 <= VehAmount ) { VehButton1.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+1].vehicleName); VehButton1.enable(); } else { VehButton1.changeLabelText( "" ); VehButton1.disable(); }
			if( CurrentName + 2 <= VehAmount ) { VehButton2.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+2].vehicleName); VehButton2.enable(); } else { VehButton2.changeLabelText( "" ); VehButton2.disable(); }
			if( CurrentName + 3 <= VehAmount ) { VehButton3.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+3].vehicleName); VehButton3.enable(); } else { VehButton3.changeLabelText( "" ); VehButton3.disable(); }
			if( CurrentName + 4 <= VehAmount ) { VehButton4.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+4].vehicleName); VehButton4.enable(); } else { VehButton4.changeLabelText( "" ); VehButton4.disable(); }
			if( CurrentName + 5 <= VehAmount ) { VehButton5.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+5].vehicleName); VehButton5.enable(); } else { VehButton5.changeLabelText( "" ); VehButton5.disable(); }
			if( CurrentName + 6 <= VehAmount ) { VehButton6.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+6].vehicleName); VehButton6.enable(); } else { VehButton6.changeLabelText( "" ); VehButton6.disable(); }
			if( CurrentName + 7 <= VehAmount ) { VehButton7.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+7].vehicleName); VehButton7.enable(); } else { VehButton7.changeLabelText( "" ); VehButton7.disable(); }
			if( CurrentName + 8 <= VehAmount ) { VehButton8.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+8].vehicleName); VehButton8.enable(); } else { VehButton8.changeLabelText( "" ); VehButton8.disable(); }
			if( CurrentName + 9 <= VehAmount ) { VehButton9.changeLabelText(GameLogic.ChassisBox.elementData[CurrentName+9].vehicleName); VehButton9.enable(); } else { VehButton9.changeLabelText( "" ); VehButton9.disable(); }
		}
		else
		if( cmd==CMD_EXIT )							//main index pages
		{
			changeGroup( -1 );
			GameLogic.changeActiveSection( GameLogic.garage );

			if(CurrentCar) CurrentCar.destroy();
			if(CurrentChassis) CurrentChassis.destroy();
		}
		else
		if( cmd==CMD_FRONTPAGE )
		{
			changeGroup( mainGroup );

			if(CurrentCar) CurrentCar.destroy();
			if(CurrentChassis) CurrentChassis.destroy();
		}
		else
		if( cmd==CMD_VEHICLE )
		{
			//pgNumberL=pageNumberL[0];
			//pgNumberR=pageNumberR[0];
			changeGroup( VehicleGroup );
			VehicleScene();
		}
		else
		if( cmd==CMD_MAIN1 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1Group );
		}
		else
		if( cmd==CMD_MAIN1_1 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1_1Group );
		}
		else
		if( cmd==CMD_MAIN1_2 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1_2Group );
		}
		else
		if( cmd==CMD_MAIN1_3 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1_3Group );
		}
		else
		if( cmd==CMD_MAIN1_4 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1_4Group );
		}
		else
		if( cmd==CMD_MAIN1_5 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1_5Group );
		}
		else
		if( cmd==CMD_MAIN1_6 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1_6Group );
		}
		else
		if( cmd==CMD_MAIN1_7 )
		{
			pgNumberL=pageNumberL[0];
			pgNumberR=pageNumberR[0];
			changeGroup( main1_7Group );
		}
		else
		if( cmd==CMD_MAIN2 )
		{
			pgNumberL=pageNumberL[1];
			pgNumberR=pageNumberR[1];
			changeGroup( main2Group );
		}
		else
		if( cmd==CMD_MAIN3 )
		{
			pgNumberL=pageNumberL[2];
			pgNumberR=pageNumberR[2];
			changeGroup( main3Group );
		}
		else
		if( cmd==CMD_MAIN4 )
		{
			pgNumberL=pageNumberL[3];
			pgNumberR=pageNumberR[3];
			changeGroup( main4Group );
		}
		else
		if( cmd==CMD_MAIN5 )
		{
			pgNumberL=pageNumberL[4];
			pgNumberR=pageNumberR[4];
			changeGroup( main5Group );
		}
		else
		if( cmd==CMD_MAIN6 )
		{
			pgNumberL=pageNumberL[5];
			pgNumberR=pageNumberR[5];
			changeGroup( main6Group );
		}
		else
		if( cmd>=CMD_DB0 && cmd<=CMD_DB7 )			//decal buttons
		{
			decalButtonPressed( cmd - CMD_DB0 );
		}
		if( cmd>=CMD_MAIN6_1 && cmd<=CMD_MAIN6_7 )	//decal subsections
		{
			String decalsDir;
			if( cmd == CMD_MAIN6_1 )
			{
				pageNumberPrefix="D-I/";
				decalsDir = "manufacturers";
			}
			else
			if( cmd == CMD_MAIN6_2 )
			{
				pageNumberPrefix="D-II/";
				decalsDir = "logos";
			}
			else
			if( cmd == CMD_MAIN6_3 )
			{
				pageNumberPrefix="D-III/";
				decalsDir = "numbers";
			}
			else
			if( cmd == CMD_MAIN6_4 )
			{
				pageNumberPrefix="D-IV/";
				decalsDir = "digits";
			}
			else
			if( cmd == CMD_MAIN6_5 )
			{
				pageNumberPrefix="D-V/";
				decalsDir = "smilies";
			}
			else
			if( cmd == CMD_MAIN6_6 )
			{
				pageNumberPrefix="D-VI/";
				decalsDir = "animals";
			}
			else
			if( cmd == CMD_MAIN6_7 )
			{
				pageNumberPrefix="D-VII/";
				decalsDir = "misc";
			}

			decals = collectDecals( decalsDir );

			curpage=1;
			showDecals=0;

			setDecalButtons();
			refreshPage();
			changeGroup( decalsGroup );
		}
		else
		if( cmd == CMD_PREVDECALPAGE )					//page control
		{
			if( showDecals >= decalButtons.length )
			{
				showDecals-=decalButtons.length;
				setDecalButtons();

				curpage-=2;
				refreshPage();
			}
		}
		else
		if( cmd == CMD_NEXTDECALPAGE )
		{
			int	max = decals.size();
			if( showDecals+decalButtons.length < max )
			{
				showDecals+=decalButtons.length;
				setDecalButtons();

				curpage+=2;
				refreshPage();
			}
		}
		else
		if( cmd == CMD_PREVPARTPAGE )
		{
			if( inventory.upScroll() )
			{
				curpage-=2;
				refreshPage();
			}
		}
		else
		if( cmd == CMD_NEXTPARTPAGE )
		{
			//biztositsuk, hogy a kov oldal is tele van (az init csak a legelsot tolti fel rendesen!)
			//ha az user cselez, atugrik masik sectionbe, aztan visszalapoz, megszivja!
			collectObjectsStep( 8 );

			if(	inventory.downScroll() )
			{
				curpage+=2;
				refreshPage();
			}
		}
		else
	// Collector filters:
		if( cmd == NO_FILTER )
		{
			inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
			step--;
		}
		else
		if( cmd == RIMS_FILTER )
		{
			Part rim = new Part();
			rim = create( null, new GameRef(parts), "0,0,0,0,0,0,", "temp rim");
			
			//System.log(rim.name);
			
			if (rim && rim instanceof Wheel)
			{
				if( Rim_size <= 14 ) // Smaller rims
				{
					if( rim.getRadius() <= 14)
					{
						inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
						step--;
					}
				}
				else
				if( Rim_size >= 23 ) // Bigger rims
				{
					if( rim.getRadius() >= 23)
					{
						inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
						step--;
					}
				}
				else // 15"-22"
				{
					if( rim.getRadius() >= Rim_size && rim.getRadius() < Rim_size+1)
					{
						inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
						step--;
					}
				}
			}
			else
			{
				step--; // Skips objects in the rims catalog that are not wheels
			}
		}
		else
		if( cmd == TIRES_FILTER )
		{
			Part tire = new Part();
			tire = create( null, new GameRef(parts), "0,0,0,0,0,0,", "temp tire");
			
			if (tire && tire instanceof Tyre)
			{
				if( Tire_size <= 14 ) // Smaller tires
				{
					if( tire.wheel_radius*2.0/25.4 <= 14)
					{
						inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
						step--;
					}
				}
				else
				if( Tire_size >= 23 ) // Bigger tires
				{
					if( tire.wheel_radius*2.0/25.4 >= 23)
					{
						inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
						step--;
					}
				}
				else // 15"-22"
				{
					if( tire.wheel_radius*2.0/25.4 >= Tire_size && tire.wheel_radius*2.0/25.4 < Tire_size+1)
					{
						inventory.items.addElement( new InventoryItem_Part( inventory, parts.id() ) );
						step--;
					}
				}
			}
			else
			{
				step--; // Skips objects in the rims catalog that are not wheels
			}
		}
		else
	// Parts collector buttons:
		if( cmd >= CMD_MAIN1_1 && cmd < CMD_MAIN6_1 ) // all parts pages buttons
		{
			collectObjectsBegin();
			if( cmd >= CMD_MAIN1_1_1 && cmd < CMD_MAIN1_2_1 ) //I4 engine part page
			{
				changeGroup( engine1Group );

				if( cmd == CMD_MAIN1_1_1 )
				{
					pageNumberPrefix="EA-I/";
					collectObjects( new GameRef(parts:0xAB5Cr) ); // Engine Blocks
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_2 )
				{
					pageNumberPrefix="EA-II/";
					collectObjects( new GameRef(parts:0xAA5Er) ); // Crankshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_3 )
				{
					pageNumberPrefix="EA-III/";
					collectObjects( new GameRef(parts:0xAB7Er) ); // Connecting Rods
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_4 )
				{
					pageNumberPrefix="EA-IV/";
					collectObjects( new GameRef(parts:0x0AB6r) ); // Pistons
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_5 )
				{
					pageNumberPrefix="EA-V/";
					collectObjects( new GameRef(parts:0xA25Dr) ); // Cylinder Heads
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_6 )
				{
					pageNumberPrefix="EA-VI/";
					collectObjects( new GameRef(parts:0xA25Fr) ); // Camshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_7 )
				{
					pageNumberPrefix="EA-VII/";
					collectObjects(  new GameRef(parts:0xA282r) ); // Exhaust Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_8 )
				{
					pageNumberPrefix="EA-VIII/";
					collectObjects( new GameRef(parts:0xA26Fr) ); // Intake Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_9 )
				{
					pageNumberPrefix="EA-IX/";
					collectObjects( new GameRef(parts:0xA26Br) ); // Fuel Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_10 )
				{
					pageNumberPrefix="EA-X/";
					collectObjects( new GameRef(parts:0x0AB4r) ); // Performance Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_11 )
				{
					pageNumberPrefix="EA-XI/";
					collectObjects( new GameRef(parts:0xA24Br) ); // Transmission
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_12 )
				{
					pageNumberPrefix="EA-XII/";
					collectObjects( new GameRef(parts:0xA23Cr) ); // Clutch and Flywheel
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_1_13 )
				{
					pageNumberPrefix="EA-XIII/";
					collectObjects( new GameRef(parts:0x0ABEr) ); // Misc
					filter = NO_FILTER;
				}
			}
			else
			if( cmd >= CMD_MAIN1_2_1 && cmd < CMD_MAIN1_3_1 ) //I6 engine part page
			{
				changeGroup( engine2Group );

				if( cmd == CMD_MAIN1_2_1 )
				{
					pageNumberPrefix="EB-I/";
					collectObjects( new GameRef(parts:0xAA5Cr) ); // Engine Blocks
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_2 )
				{
					pageNumberPrefix="EB-II/";
					collectObjects( new GameRef(parts:0xAB5Er) ); // Crankshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_3 )
				{
					pageNumberPrefix="EB-III/";
					collectObjects( new GameRef(parts:0xBB7Er) ); // Connecting Rods
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_4 )
				{
					pageNumberPrefix="EB-IV/";
					collectObjects( new GameRef(parts:0x0BB6r) ); // Pistons
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_5 )
				{
					pageNumberPrefix="EB-V/";
					collectObjects( new GameRef(parts:0xB25Dr) ); // Cylinder Heads
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_6 )
				{
					pageNumberPrefix="EB-VI/";
					collectObjects( new GameRef(parts:0xB25Fr) ); // Camshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_7 )
				{
					pageNumberPrefix="EB-VII/";
					collectObjects(  new GameRef(parts:0xB282r) ); // Exhaust Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_8 )
				{
					pageNumberPrefix="EB-VIII/";
					collectObjects( new GameRef(parts:0xB26Fr) ); // Intake Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_9 )
				{
					pageNumberPrefix="EB-IX/";
					collectObjects( new GameRef(parts:0xB26Br) ); // Fuel Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_10 )
				{
					pageNumberPrefix="EB-X/";
					collectObjects( new GameRef(parts:0x0BB4r) ); // Performance Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_11 )
				{
					pageNumberPrefix="EB-XI/";
					collectObjects( new GameRef(parts:0xB24Br) ); // Transmission
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_12 )
				{
					pageNumberPrefix="EB-XII/";
					collectObjects( new GameRef(parts:0xB23Cr) ); // Clutch and Flywheel
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_2_13 )
				{
					pageNumberPrefix="EB-XIII/";
					collectObjects( new GameRef(parts:0x0BBEr) ); // Misc
					filter = NO_FILTER;
				}
			}
			else
			if( cmd >= CMD_MAIN1_3_1 && cmd < CMD_MAIN1_4_1 ) //V6 engine part page
			{
				changeGroup( engine3Group );

				if( cmd == CMD_MAIN1_3_1 )
				{
					pageNumberPrefix="ED-I/";
					collectObjects( new GameRef(parts:0xAD5Cr) ); // Engine Blocks
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_2 )
				{
					pageNumberPrefix="ED-II/";
					collectObjects( new GameRef(parts:0xAD5Er) ); // Crankshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_3 )
				{
					pageNumberPrefix="ED-III/";
					collectObjects( new GameRef(parts:0xDB7Er) ); // Connecting Rods
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_4 )
				{
					pageNumberPrefix="ED-IV/";
					collectObjects( new GameRef(parts:0x0DB6r) ); // Pistons
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_5 )
				{
					pageNumberPrefix="ED-V/";
					collectObjects( new GameRef(parts:0xD25Dr) ); // Cylinder Heads
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_6 )
				{
					pageNumberPrefix="ED-VI/";
					collectObjects( new GameRef(parts:0xD25Fr) ); // Camshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_7 )
				{
					pageNumberPrefix="ED-VII/";
					collectObjects(  new GameRef(parts:0xD282r) ); // Exhaust Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_8 )
				{
					pageNumberPrefix="ED-VIII/";
					collectObjects( new GameRef(parts:0xD26Fr) ); // Intake Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_9 )
				{
					pageNumberPrefix="ED-IX/";
					collectObjects( new GameRef(parts:0xD26Br) ); // Fuel Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_10 )
				{
					pageNumberPrefix="ED-X/";
					collectObjects( new GameRef(parts:0x0DB4r) ); // Performance Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_11 )
				{
					pageNumberPrefix="ED-XI/";
					collectObjects( new GameRef(parts:0xD24Br) ); // Transmission
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_12 )
				{
					pageNumberPrefix="ED-XII/";
					collectObjects( new GameRef(parts:0xD23Cr) ); // Clutch and Flywheel
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_3_13 )
				{
					pageNumberPrefix="ED-XIII/";
					collectObjects( new GameRef(parts:0x0DBEr) ); // Misc
					filter = NO_FILTER;
				}
			}
			else
			if( cmd >= CMD_MAIN1_4_1 && cmd < CMD_MAIN1_5_1 ) //V8 engine part page
			{
				changeGroup( engine4Group );

				if( cmd == CMD_MAIN1_4_1 )
				{
					pageNumberPrefix="EC-I/";
					collectObjects( new GameRef(parts:0xAC5Cr) ); // Engine Blocks
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_2 )
				{
					pageNumberPrefix="EC-II/";
					collectObjects( new GameRef(parts:0xAC5Er) ); // Crankshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_3 )
				{
					pageNumberPrefix="EC-III/";
					collectObjects( new GameRef(parts:0xCB7Er) ); // Connecting Rods
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_4 )
				{
					pageNumberPrefix="EC-IV/";
					collectObjects( new GameRef(parts:0x0CB6r) ); // Pistons
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_5 )
				{
					pageNumberPrefix="EC-V/";
					collectObjects( new GameRef(parts:0xC25Dr) ); // Cylinder Heads
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_6 )
				{
					pageNumberPrefix="EC-VI/";
					collectObjects( new GameRef(parts:0xC25Fr) ); // Camshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_7 )
				{
					pageNumberPrefix="EC-VII/";
					collectObjects(  new GameRef(parts:0xC282r) ); // Exhaust Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_8 )
				{
					pageNumberPrefix="EC-VIII/";
					collectObjects( new GameRef(parts:0xC26Fr) ); // Intake Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_9 )
				{
					pageNumberPrefix="EC-IX/";
					collectObjects( new GameRef(parts:0xC26Br) ); // Fuel Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_10 )
				{
					pageNumberPrefix="EC-X/";
					collectObjects( new GameRef(parts:0x0CB4r) ); // Performance Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_11 )
				{
					pageNumberPrefix="EC-XI/";
					collectObjects( new GameRef(parts:0xC24Br) ); // Transmission
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_12 )
				{
					pageNumberPrefix="EC-XII/";
					collectObjects( new GameRef(parts:0xC23Cr) ); // Clutch and Flywheel
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_4_13 )
				{
					pageNumberPrefix="EC-XIII/";
					collectObjects( new GameRef(parts:0x0CBEr) ); // Misc
					filter = NO_FILTER;
				}
			}
			else
			if( cmd >= CMD_MAIN1_5_1 && cmd < CMD_MAIN1_6_1 ) //V10 engine part page
			{
				changeGroup( engine5Group );

				if( cmd == CMD_MAIN1_5_1 )
				{
					pageNumberPrefix="EE-I/";
					collectObjects( new GameRef(parts:0xAE5Cr) ); // Engine Blocks
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_2 )
				{
					pageNumberPrefix="EE-II/";
					collectObjects( new GameRef(parts:0xAE5Er) ); // Crankshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_3 )
				{
					pageNumberPrefix="EE-III/";
					collectObjects( new GameRef(parts:0xEB7Er) ); // Connecting Rods
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_4 )
				{
					pageNumberPrefix="EE-IV/";
					collectObjects( new GameRef(parts:0x0EB6r) ); // Pistons
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_5 )
				{
					pageNumberPrefix="EE-V/";
					collectObjects( new GameRef(parts:0xE25Dr) ); // Cylinder Heads
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_6 )
				{
					pageNumberPrefix="EE-VI/";
					collectObjects( new GameRef(parts:0xE25Fr) ); // Camshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_7 )
				{
					pageNumberPrefix="EE-VII/";
					collectObjects(  new GameRef(parts:0xE282r) ); // Exhaust Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_8 )
				{
					pageNumberPrefix="EE-VIII/";
					collectObjects( new GameRef(parts:0xE26Fr) ); // Intake Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_9 )
				{
					pageNumberPrefix="EE-IX/";
					collectObjects( new GameRef(parts:0xE26Br) ); // Fuel Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_10 )
				{
					pageNumberPrefix="EE-X/";
					collectObjects( new GameRef(parts:0x0EB4r) ); // Performance Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_11 )
				{
					pageNumberPrefix="EE-XI/";
					collectObjects( new GameRef(parts:0xE24Br) ); // Transmission
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_12 )
				{
					pageNumberPrefix="EE-XII/";
					collectObjects( new GameRef(parts:0xE23Cr) ); // Clutch and Flywheel
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_5_13 )
				{
					pageNumberPrefix="EE-XIII/";
					collectObjects( new GameRef(parts:0x0EBEr) ); // Misc
					filter = NO_FILTER;
				}
			}
			else
			if( cmd >= CMD_MAIN1_6_1 && cmd < CMD_MAIN1_7_1 ) //V12 engine part page
			{
				changeGroup( engine6Group );

				if( cmd == CMD_MAIN1_6_1 )
				{
					pageNumberPrefix="EF-I/";
					collectObjects( new GameRef(parts:0xAF5Cr) ); // Engine Blocks
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_2 )
				{
					pageNumberPrefix="EF-II/";
					collectObjects( new GameRef(parts:0xAF5Er) ); // Crankshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_3 )
				{
					pageNumberPrefix="EF-III/";
					collectObjects( new GameRef(parts:0xFB7Er) ); // Connecting Rods
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_4 )
				{
					pageNumberPrefix="EF-IV/";
					collectObjects( new GameRef(parts:0x0FB6r) ); // Pistons
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_5 )
				{
					pageNumberPrefix="EF-V/";
					collectObjects( new GameRef(parts:0xF25Dr) ); // Cylinder Heads
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_6 )
				{
					pageNumberPrefix="EF-VI/";
					collectObjects( new GameRef(parts:0xF25Fr) ); // Camshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_7 )
				{
					pageNumberPrefix="EF-VII/";
					collectObjects(  new GameRef(parts:0xF282r) ); // Exhaust Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_8 )
				{
					pageNumberPrefix="EF-VIII/";
					collectObjects( new GameRef(parts:0xF26Fr) ); // Intake Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_9 )
				{
					pageNumberPrefix="EF-IX/";
					collectObjects( new GameRef(parts:0xF26Br) ); // Fuel Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_10 )
				{
					pageNumberPrefix="EF-X/";
					collectObjects( new GameRef(parts:0x0FB4r) ); // Performance Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_11 )
				{
					pageNumberPrefix="EF-XI/";
					collectObjects( new GameRef(parts:0xF24Br) ); // Transmission
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_12 )
				{
					pageNumberPrefix="EF-XII/";
					collectObjects( new GameRef(parts:0xF23Cr) ); // Clutch and Flywheel
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_6_13 )
				{
					pageNumberPrefix="EF-XIII/";
					collectObjects( new GameRef(parts:0x0FBEr) ); // Misc
					filter = NO_FILTER;
				}
			}
			else
			if( cmd >= CMD_MAIN1_7_1 && cmd < CMD_MAIN2_1 ) //V16 engine part page
			{
				changeGroup( engine7Group );

				if( cmd == CMD_MAIN1_7_1 )
				{
					pageNumberPrefix="EG-I/";
					collectObjects( new GameRef(parts:0xA05Cr), new GameRef(parts:0x025Cr) ); // Engine Blocks
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_2 )
				{
					pageNumberPrefix="EG-II/";
					collectObjects( new GameRef(parts:0xA05Er), new GameRef(parts:0x025Er) ); // Crankshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_3 )
				{
					pageNumberPrefix="EG-III/";
					collectObjects( new GameRef(parts:0x0B7Er), new GameRef(parts:0x00B7r) ); // Connecting Rods
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_4 )
				{
					pageNumberPrefix="EG-IV/";
					collectObjects( new GameRef(parts:0x00B6r) ); // Pistons
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_5 )
				{
					pageNumberPrefix="EG-V/";
					collectObjects( new GameRef(parts:0x025Dr) ); // Cylinder Heads
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_6 )
				{
					pageNumberPrefix="EG-VI/";
					collectObjects( new GameRef(parts:0x025Fr) ); // Camshafts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_7 )
				{
					pageNumberPrefix="EG-VII/";
					collectObjects(  new GameRef(parts:0x0282r) ); // Exhaust Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_8 )
				{
					pageNumberPrefix="EG-VIII/";
					collectObjects( new GameRef(parts:0x026Fr), new GameRef(parts:0x0255r) ); // Intake Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_9 )
				{
					pageNumberPrefix="EG-IX/";
					collectObjects( new GameRef(parts:0x026Br), new GameRef(parts:0x026Er) ); // Fuel Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_10 )
				{
					pageNumberPrefix="EG-X/";
					collectObjects( new GameRef(parts:0x00B4r), new GameRef(parts:0x0279r), new GameRef(parts:0x0281r), new GameRef(parts:0x00BDr) ); // Performance Systems
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_11 )
				{
					pageNumberPrefix="EG-XI/";
					collectObjects( new GameRef(parts:0x024Br), new GameRef(parts:0x023Br), new GameRef(parts:0x024Ar) ); // Transmission
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_12 )
				{
					pageNumberPrefix="EG-XII/";
					collectObjects( new GameRef(parts:0x023Cr), new GameRef(parts:0x0249r) ); // Clutch and Flywheel
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN1_7_13 )
				{
					pageNumberPrefix="EG-XIII/";
					collectObjects( new GameRef(parts:0x00BEr), new GameRef(parts:0x0012r), new GameRef(parts:0xD122r), new GameRef(parts:0x0010r), new GameRef(parts:0x0252r) ); // Misc
					filter = NO_FILTER;
				}
			}
			else
			if( cmd == CMD_MAIN1_8 )
			{
				changeGroup( parts1Group );
				pageNumberPrefix="EK/";
				collectObjects( new GameRef(parts:0xF23Cr) ); // Engine Kits
				filter = NO_FILTER;
			}
			else
			if( cmd >= CMD_MAIN2_1 && cmd < CMD_MAIN3_1 )		//body subs
			{
				changeGroup( parts2Group );
				if( cmd == CMD_MAIN2_1 )					
				{
					pageNumberPrefix="B-I/";
					collectObjects( new GameRef(parts:0xF242r) ); // Body Panels
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN2_2 )					
				{
					pageNumberPrefix="B-II/";
					collectObjects( new GameRef(parts:0xF24Fr) ); // Replacement Parts
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN2_3 )					
				{
					pageNumberPrefix="B-III/";
					collectObjects( new GameRef(parts:0xF24Cr) ); // Lights & Windows
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN2_4 )				
				{
					pageNumberPrefix="B-IV/";
					collectObjects( new GameRef(parts:0xF228r) ); // Neon Lights
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN2_5 )				
				{
					pageNumberPrefix="B-V/";
					collectObjects( new GameRef(parts:0xF233r) ); // Aerodynamic Tuning
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN2_6 )				
				{
					pageNumberPrefix="B-VI/";
					collectObjects( new GameRef(parts:0x00C9r), new GameRef(parts:0x0250r) ); // Mufflers
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN2_7 )				
				{
					pageNumberPrefix="B-VII/";
					collectObjects( new GameRef(parts:0xF23Br) ); // Body Kits
					filter = NO_FILTER;
				}
			}
			if( cmd >= CMD_MAIN3_1 && cmd < CMD_MAIN4_1 )
			{
				changeGroup( parts3Group );

				if( cmd >= CMD_MAIN3_9 && cmd <= CMD_MAIN3_18 )	//all Rims buttons
				{
					//collectObjectsBegin(); //not sure why this is here
					collectObjects( new GameRef(parts:0xF235r), new GameRef(parts:0xF23Dr) );
					filter = RIMS_FILTER;

					int buttonNumber;

					if( cmd == CMD_MAIN3_9 ) buttonNumber = 14; // Smaller than 15"
					if( cmd == CMD_MAIN3_10 ) buttonNumber = 15;
					if( cmd == CMD_MAIN3_11 ) buttonNumber = 16;
					if( cmd == CMD_MAIN3_12 ) buttonNumber = 17;
					if( cmd == CMD_MAIN3_13 ) buttonNumber = 18;
					if( cmd == CMD_MAIN3_14 ) buttonNumber = 19;
					if( cmd == CMD_MAIN3_15 ) buttonNumber = 20;
					if( cmd == CMD_MAIN3_16 ) buttonNumber = 21;
					if( cmd == CMD_MAIN3_17 ) buttonNumber = 22;
					if( cmd == CMD_MAIN3_18 ) buttonNumber = 23; // Bigger than 22"

					Rim_size = buttonNumber; // Info for RIMS_FILTER
					pageNumberPrefix="R-R"+buttonNumber+"''/";
				}
				else
				if( cmd >= CMD_MAIN3_19 && cmd <= CMD_MAIN3_28 )	//all Tires buttons
				{
					//collectObjectsBegin(); //not sure why this is here
					collectObjects( new GameRef(parts:0xF23Er) );
					filter = TIRES_FILTER;

					int buttonNumber;

					if( cmd == CMD_MAIN3_19 ) buttonNumber = 14; // Smaller than 15"
					if( cmd == CMD_MAIN3_20 ) buttonNumber = 15;
					if( cmd == CMD_MAIN3_21 ) buttonNumber = 16;
					if( cmd == CMD_MAIN3_22 ) buttonNumber = 17;
					if( cmd == CMD_MAIN3_23 ) buttonNumber = 18;
					if( cmd == CMD_MAIN3_24 ) buttonNumber = 19;
					if( cmd == CMD_MAIN3_25 ) buttonNumber = 20;
					if( cmd == CMD_MAIN3_26 ) buttonNumber = 21;
					if( cmd == CMD_MAIN3_27 ) buttonNumber = 22;
					if( cmd == CMD_MAIN3_28 ) buttonNumber = 23; // Bigger than 22"

					Tire_size = buttonNumber; // Info for TIRES_FILTER
					pageNumberPrefix="R-T"+buttonNumber+"''/";
				}
				else
				if( cmd == CMD_MAIN3_1 )	//running gear subsections
				{
					pageNumberPrefix="R-I/";
					collectObjects( new GameRef(parts:0xF229r) ); // Suspension
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN3_2 )
				{
					pageNumberPrefix="R-II/";
					collectObjects( new GameRef(parts:0x001Cr) ); // Shock Absorbers
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN3_3 )
				{
					pageNumberPrefix="R-III/";
					collectObjects( new GameRef(parts:0xF22Ar) ); // Springs
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN3_4 )
				{
					pageNumberPrefix="R-IV/";
					collectObjects( new GameRef(parts:0xF22Dr) ); // Brakes
					filter = NO_FILTER;
				}
				else /*
				if( cmd == CMD_MAIN3_5 )
				{
					pageNumberPrefix="R-V/";
					collectObjects( new GameRef(parts:0xF23Er) ); // Tyres
					filter = NO_FILTER;
				}
				else */
				if( cmd == CMD_MAIN3_6 )
				{
					pageNumberPrefix="R-V/";
					collectObjects( new GameRef(parts:0xD120r) ); // Others
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN3_7 )
				{
					pageNumberPrefix="R-VI/";
					collectObjects( new GameRef(parts:0xD121r) ); // Pro tuning
					filter = NO_FILTER;
				}
				if( cmd == CMD_MAIN3_8 )
				{
					pageNumberPrefix="R-VII/";
					collectObjects( new GameRef(parts:0xF249r) ); // RG kits
					filter = NO_FILTER;
				}
			}
			else
			if( cmd >= CMD_MAIN4_1 && cmd < CMD_MAIN5_1 )
			{
				changeGroup( parts4Group );
				if( cmd == CMD_MAIN4_1 )						//interior subsection
				{
					pageNumberPrefix="I-I/";
					collectObjects( new GameRef(parts:0xF243r) );	//steerwheels
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN4_2 )
				{
					pageNumberPrefix="I-II/";
					collectObjects( new GameRef(parts:0xF245r) );	//dashes
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN4_3 )
				{
					pageNumberPrefix="I-III/";
					collectObjects( new GameRef(parts:0xF23fr) );	//gearknobs
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN4_4 )
				{
					pageNumberPrefix="I-IV/";
					collectObjects( new GameRef(parts:0xF241r) );	//pedals
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN4_5 )
				{
					pageNumberPrefix="I-V/";
					collectObjects( new GameRef(parts:0xF244r) );	//gauges
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN4_6 )
				{
					pageNumberPrefix="I-VI/";
					collectObjects( new GameRef(parts:0xF246r) );	//seats
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN4_7 )
				{
					pageNumberPrefix="I-VII/";
					collectObjects( new GameRef(parts:0xF247r) );	//rollbars
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN4_8 )
				{
					pageNumberPrefix="I-VIII/";
					collectObjects( new GameRef(parts:0xF248r) );	//decoration
					filter = NO_FILTER;
				}
			}
			if( cmd >= CMD_MAIN5_1 && cmd < CMD_MAIN6_1 ) //audio subsection
			{
				changeGroup( parts5Group );
				if( cmd == CMD_MAIN5_1 )				
				{
					pageNumberPrefix="A-I/";
					collectObjects( new GameRef(parts:0xF240r) );	//head units
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN5_2 )
				{
					pageNumberPrefix="A-II/";
					collectObjects( new GameRef(parts:0x0246r) );	// Amplifiers
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN5_3 )
				{
					pageNumberPrefix="A-III/";
					collectObjects( new GameRef(parts:0x0247r) );	// Boxes
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN5_4 )
				{
					pageNumberPrefix="A-IV/";
					collectObjects( new GameRef(parts:0x0248r) );	// Subwoofers
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN5_5 )
				{
					pageNumberPrefix="A-V/";
					collectObjects( new GameRef(parts:0x024Br) );	// Accesories
					filter = NO_FILTER;
				}
				else
				if( cmd == CMD_MAIN5_6 )
				{
					pageNumberPrefix="A-VI/";
					collectObjects( new GameRef(parts:0x025Br) );	// Audio Kits
					filter = NO_FILTER;
				}
			}

			collectObjectsEnd();
			refreshPage();
		}
	}
}

//----------------------------------------------------------------------------------------------
//----------------------------------------------------------------------------------------------
//----------------------------------------------------------------------------------------------

public class CatalogInventory extends VisualInventory
{
	Osd			osd;	//for da namez
	TextBox[]	names;

	Catalog	catalog;

	public CatalogInventory( Catalog cat, Player player, float left, float top, float width, float height )
	{
		super(player, left, top, width, height );
		catalog=cat;
	}
		
	public void initVisuals( float left, float top, float width, float height  )
	{
		linesPerPage=2;
		partsPerLine=4;

		//mely itemek lehetnek lathatoak kezdetben?
		cline=0;
		start = cline * partsPerLine;
		stop = start + linesPerPage * partsPerLine;

		//0..1 viewport coordinatarendszerben!
		float	itemWidth=0.20, itemHeight=0.15;
		float	hSpacing = (width-itemWidth*partsPerLine)/partsPerLine;
		float	vSpacing = (height-itemHeight*linesPerPage)/(linesPerPage-1);
		
		//backObject = new RenderRef( misc.catalog:0x00000020r );
		panels=new InventoryPanel[partsPerLine*linesPerPage];
		names=new TextBox[panels.length];

		//eggyel magasabb pri vp kell, mint maga a katalogus, kulonben eltunnek a szovegek!
		osd = new Osd( 1.0, 0.0, 11 );
		osd.iLevel = Osd.IL_NONE;

		int	index;
		float cheight=top;
		for( int i=0; i<linesPerPage; i++ )
		{
			float cwidth=left;
			for( int j=0; j<partsPerLine; j++ )
			{
				if( j == partsPerLine/2 )
					cwidth+=hSpacing;

				index = i*partsPerLine+j; 
				panels[index]=new CatalogInventoryPanel( this, index, cwidth, cheight, itemWidth, itemHeight, player );
				names[index]=osd.createTextBox( null, Frontend.smallFont, Text.ALIGN_CENTER, (cwidth)*2-1, ((cheight+itemHeight)*2)-1, itemWidth*2 );
				//kicsit patch, sajnos a fontnak nics olyan propertyje, hogy multicolor-e!
				if( Frontend.smallFont.id() == Text.RID_CONSOLE10 || Frontend.smallFont.id() == Text.RID_CONSOLE5 )
					names[index].changeColor( 0xFF000000 );

				cwidth+=itemWidth+hSpacing;
			}
			cheight+=itemHeight+vSpacing;
		}
	}

	public int upScroll()
	{
		if( cline )
		{
			cline-=linesPerPage;
			update();
			return 1;
		}
		return 0;
	}

	public int downScroll()
	{
		if( cline+linesPerPage < pages()*linesPerPage )
		{
			cline+=linesPerPage;
			update();
			return 1;
		}
		return 0;
	}

	//number of twin pages in a catalog section
	public int	pages()
	{
		if( items.size() > 1)
			return (items.size()-1)/(partsPerLine*linesPerPage)+1;
			
		return 1;
	}

	public void panelLeftClick( int index )
	{
		index += currentLine()*partsPerLine;

		if( index<items.size() )
		{
			InventoryItem item = items.elementAt( index );

			int price = item.getPrice() * Catalog.PRICERATIO;
			if( price <= player.money )
			{
				Dialog d = new BuyCatalogItemDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG|Dialog.DF_WIDE, item, price );

				if( d.display() == 0 )
				{
					player.money-= price;
					item.copyToInventory( player.parts );
					catalog.refreshMoneyString();
					//kap egy bonus matricat is, ha spec alkatresz:
					int decalID = item.getLogo();
					if( decalID )
					{
						player.decals.addElement( new Decal( decalID ) );
					}
				}
			}
			else
			{
				new WarningDialog( player.controller, Dialog.DF_MODAL|Dialog.DF_DEFAULTBG, "NOT ENOUGHT MONEY", "You don't have enought money to buy this part!" ).display();
			}
		}
	}

	public void update()
	{
		//hol kapcsolodnak ki a buttonok?
		int begin = start;
		int end = stop;

		//mibol lesznek buttonok?
		start = cline * partsPerLine;
		stop = start + linesPerPage * partsPerLine;

		int i, vis;

		//clear changed ones
		vis=0;
		for( i=begin; i<end; i++ )
		{
			names[vis++].changeText( null );
		}

		//add new ones
		vis=0;
		for( i=start; i<stop; i++ )
		{
			if( i<items.size() )
			{
				InventoryItem item = items.elementAt(i);
				names[vis++].changeText( item.getName() + " $"+ (int)(item.getPrice() * Catalog.PRICERATIO) );
			}
		}

		super.update();
	}

	public void show()
	{
		super.show();
		osd.show();
	}

	public void hide()
	{
		osd.hide();
		super.hide();
	}
}

//----------------------------------------------------------------------------------------------

public class CatalogInventoryPanel extends InventoryPanel
{
	Player	player;

	ResourceRef	infoTex0, infoTex1;
	Rectangle infoRect0, infoRect1;

	public CatalogInventoryPanel( CatalogInventory inventory, int index, float left, float top, float width, float height, Player p )
	{
		super( inventory, index, left, top, width, height );

		infoRect0 = inventory.osd.createRectangle( (left+width/2)*2-1, ((top-0.04)*2)-1, 0.1, 0.1, 1.0, -0.5, 0.0, 0, null );
		infoRect1 = inventory.osd.createRectangle( (left+width/2)*2-1, ((top-0.04)*2)-1, 0.1, 0.1, 1.0,  0.5, 0.0, 0, null );
		player = p;
	}

	public void swap( int index_a, int index_b )
	{
		//nincs swap!!
	}

	public void attachItem( InventoryItem invitem )
	{
		ypr = new Ypr( -1.4, -0.7, 0.0 );

		super.attachItem( invitem );

		infoTex0 = infoTex1 = null;

		if( invitem && invitem instanceof InventoryItem_Part)
		{
			((InventoryItem_Part)invitem).compatibility = 0;

			Part p=invitem.getPart();
			if( player.car )
				if( p.getInfo( p.GII_COMPATIBLE, player.car.id() + "" ) )
				{
					if( p.getInfo( p.GII_INSTALL_OK, player.car.id() + "" ) )
					{
						infoTex0 = Catalog.RR_ICON_CAR_1STEP;
						((InventoryItem_Part)invitem).compatibility |= 1;
					}
					else
					{
						infoTex0 = Catalog.RR_ICON_CAR_COMP;
						((InventoryItem_Part)invitem).compatibility |= 2;
					}
				}


			int compatibleParts;
			for( int i=player.parts.size()-1; i>=0; i-- )
			{
				if( player.parts.items.elementAt(i) instanceof InventoryItem_Part )
					if( p.getInfo( p.GII_INSTALL_OK, player.parts.items.elementAt(i).getPart().id() + "" ) )
					{
						compatibleParts++;
						((InventoryItem_Part)invitem).compatibility |= 4;
						break;	//inkabb ne nezzuk tovabb, lassit
					}
			}

			if( compatibleParts )
				infoTex1 = Catalog.RR_ICON_INV_COMP;
			
		}

		infoRect0.changeTexture( infoTex0 );
		infoRect1.changeTexture( infoTex1 );
	}

}

public class BuyCatalogItemDialog extends YesNoDialog
{
	public BuyCatalogItemDialog( Controller ctrl, int myflags, InventoryItem item, int price )
	{ 
		super( ctrl, myflags, "BUY PART", genBody( item, price) ); 
	}

	public String genBody( InventoryItem item, int price )
	{
		String body = "Do you want to buy this " + item.getName()  + " for $" + price + " ? \n \n \n" + item.getDescription() + " \n \n Note: ";
//		String body = "Do you want to buy this " + item.getName()  + " for $" + price + " ? \n \n Note: ";		

		if( item instanceof InventoryItem_Part )
		{
			if( !((InventoryItem_Part)item).compatibility )
				body = body + "NOT compatible with you current car / parts bin!";
			else
			{
				if( ((InventoryItem_Part)item).compatibility & 1 )
					body = body + "installable right away to your car";
				if( ((InventoryItem_Part)item).compatibility & 2 )
					body = body + "interchangeable with your cars parts";
				if( ((InventoryItem_Part)item).compatibility & 4 )
				{
					if( ((InventoryItem_Part)item).compatibility & 3 )
						body = body + " and ";
					body = body + "parts bin compatible";
				}

			}
		}

		return body;
	}
}