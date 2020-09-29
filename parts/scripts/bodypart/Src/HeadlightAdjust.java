package java.game.parts.bodypart;

import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.render.osd.*;
import java.game.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;
import java.game.parts.rgearpart.reciprocatingrgearpart.*;


public class HeadlightAdjust extends Headlights
{
	// HeadlightAdjust script by RedCarDriver. Version 2018-11-19.
	
	public HeadlightAdjust( int id )
	{
		super( id );
		carCategory = PACKAGE;
		name = "Headlight Adjustment Module";
		description = "Allows adjustments to headlight controller (trunk). Class by Harrison15.";
	}

	public Part getHeadlightController()
	{
		Part res = partOnSlot(7); //CHANGE TO PARENT SLOT OF THIS PART

		if (res && res instanceof HeadlightController) //CHANGE TO CORRECT PARENT CLASS
		{
			return res;
		}
//		else
//		{
//			System.log("!!!Headlight not attached to headlight controller!!!");
//		}
		return null;
	}

	public int isTuneable()
	{
		Part lightcontrol = this.getHeadlightController();
		if (lightcontrol)
			return true;
		return false;
	}
	
	float old_rads;
	
	float old_open_mode;
	
	public void buildTuningMenu( Menu m )
	{
		Part lightcontrol = getHeadlightController();

		if (lightcontrol && lightcontrol instanceof HeadlightController)
		{
			lightcontrol.debugopen();
		
			old_rads = lightcontrol.getRads();
			lightcontrol.setDegs(rad2deg(lightcontrol.getRads()));

			old_open_mode = lightcontrol.getOpenMode();
		
			String[]	headlightmodeText = new String[4];
			headlightmodeText[0] = "Standard (Full Open/Close)";
			headlightmodeText[1] = "Full Close, Custom Angle When Open";
			headlightmodeText[2] = "Custom Angle When Closed, Full Open";
			headlightmodeText[3] = "Custom Angle Full-Time";

			m.addItem( "Headlight Angle", 1, lightcontrol.getDegs(), (rad2deg(lightcontrol.getFullCloseRads())), (rad2deg(lightcontrol.getFullOpenRads())), 1.1, null ).printValue("   %1.1f degrees");

			m.addItem( "Headlight Mode: ", 2, lightcontrol.getOpenMode(), headlightmodeText, null );
	
			m.addItem( "Reset to factory defaults",			0);	//this should always be with cmd=0
		
			GameLogic.player.car.wakeUp();
		}
	}

	public void endTuningSession( int cancelled )
	{
		Part lightcontrol = getHeadlightController();
		
		if( cancelled )
		{
			lightcontrol.setRads(old_rads);
			lightcontrol.setOpenMode(old_open_mode);
		}
		else
		{
			if (lightcontrol.getRads() != old_rads)
				GameLogic.spendTime(5*60);
			if (lightcontrol.getOpenMode() != old_open_mode)
				GameLogic.spendTime(5*60);
			getCar_LocalVersion();
			if (the_car)
				the_car.forceUpdate();
		}
		lightcontrol.resetAnim();
		// Debugging //
//		lightcontrol.setRads((fullopenrads + fullcloserads)/2);
//		lightcontrol.setOpenMode(1);
	}

	public void handleMessage( Event m )
	{
		Part lightcontrol = this.getHeadlightController();

		if( m.cmd == 0 )
		{
			lightcontrol.setRads(lightcontrol.getFullOpenRads());
			m.gadget.osd.findGadget( this, 1 ).setValue( rad2deg(lightcontrol.getFullOpenRads()) );
			m.gadget.osd.findGadget( this, 2 ).setValue( 0 );
			
			lightcontrol.setOpenMode(0);
		}
		else
		if( m.cmd == 1 )
		{
			lightcontrol.setRads(deg2rad(((Slider)m.gadget).value));
			lightcontrol.setDegs(((Slider)m.gadget).value);
			((Slider)m.gadget).changeVLabelText( Float.toString(lightcontrol.getDegs(), "   %1.1f degrees"));
		}
		else
		if( m.cmd == 2 )
		{
			lightcontrol.setOpenMode(((MultiChoice)m.gadget).value);
		}
	}
	
	public float rad2deg( float rad )
	{
		return(rad * 180 / 3.141);
	}
	public float deg2rad( float deg )
	{
		return(deg * 3.141 / 180);
	}
}
