package java.game;

import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.*;
import java.render.*;	//Text
import java.render.osd.*;	//Text
import java.sound.*;
import java.render.osd.dialog.*; //Dialog Window

import java.game.parts.*;
import java.game.parts.enginepart.*;

public class Drag_Track extends Track
{	
	final static String MapName = "Drag Track";
	final static String MapDescription = "\n \n  Drag Track Reloaded! \n Finally preety old Wichur's project converted to game by Miran";
	final static String MapAuthor = "Author: Wichur & Miran";
	final static ResourceRef MapImage = new ResourceRef(Maps.Addon_Maps.Drag_Track:0x0DC2r); // Image for track selector
	final static float	TS_Version = 1.0; // Dont change it!

	public Drag_Track()
	{
		posStart = new Vector3( 30.0, -0.5, -30.0 ); // Start car position
		oriStart = new Ypr( 0.0, 0.0, 0.0 ); // Start car rotation

		map = new GroundRef( Maps.Addon_Maps.Drag_Track:0x00000001r );
	}

	public void StartMap()
	{ // Track selector run this void when you click "TAKE IT"
		GameLogic.changeActiveSection( new Drag_Track() );
	}

	public void enter( GameState prev_state )
	{
		Frontend.loadingScreen.show();
		GfxEngine.flush();

		map.setWater(new Vector3(0.0,-8.4,-1500.0), new Vector3(0.0,1.0,0.0), 300.0, 50.0); // Water object and behavior

		super.enter( prev_state );
		
		float hour = GameLogic.getTime()/3600;
		if ( hour > 5 && hour < 21 )
			map.setFog( 0x00A0B0B0, 0.0, 1000.0 );
		else
			map.setFog( 0x000A0A1A, 0.0, 1000.0 );
	}

	public void exit( GameState next_state )
	{
		super.exit( next_state );
	}
}


