package java.game.parts.enginepart;

import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;

public class OilPan extends EnginePart
{
	float	friction_fwd	= 1.0;
	float	friction_rev	= 1.0;
	float	capacity	= 0.0;
	
	float posX, posY, posZ, rotY, rotP, rotR = 0.0;

	public OilPan( int id )
	{
		super( id );

		name = "Oil pan";

		prestige_calc_weight = 7.5;
	}
	
	
	public void load( File saveGame )
	{
		super.load( saveGame );

		int	save_ver = saveGame.readInt();

		if (save_ver >= 1)
		{
			posX	= saveGame.readFloat();
			posY	= saveGame.readFloat();
			posZ	= saveGame.readFloat();
			rotY	= saveGame.readFloat();
			rotP	= saveGame.readFloat();
			rotR	= saveGame.readFloat();
		}
	}

	public void save( File saveGame )
	{
		super.save( saveGame );

		int	save_ver = 1;

		saveGame.write( save_ver );
		if (save_ver >= 1)
		{	
			saveGame.write( posX );
			saveGame.write( posY );
			saveGame.write( posZ );
			saveGame.write( rotY );
			saveGame.write( rotP );
			saveGame.write( rotR );
		}
	}
	
	
	
	public int isTuneable()
	{
		return 1;
	}
	
	
	float old_posX, old_posY, old_posZ, old_rotY, old_rotP, old_rotR;
	
	public void buildTuningMenu( Menu m )
	{
		Slider s;
		
		old_posX = posX;
		old_posY = posY;
		old_posZ = posZ;
		old_rotY = rotY;
		old_rotP = rotP;
		old_rotR = rotR;

		m.addSeparator();
		
		s = m.addItem( "X", 1, posX, -2.0, 2.0, 1501, null );
		s.changeVLabelText( Float.toString(posX, "   %1.3f") );
		
		s = m.addItem( "Y", 2, posY, -2.0, 2.0, 1501, null );
		s.changeVLabelText( Float.toString(posY, "   %1.3f") );
		
		s = m.addItem( "Z", 3, posZ, -2.0, 2.0, 1501, null );
		s.changeVLabelText( Float.toString(posZ, "   %1.3f") );
		
		s = m.addItem( "Y", 4, rotY, -180.0, 180, 361, null );
		s.changeVLabelText( Float.toString(rotY, "   %1.0f degrees") );
		
		s = m.addItem( "P", 5, rotP, -180.0, 180, 361, null );
		s.changeVLabelText( Float.toString(rotP, "   %1.0f degrees") );
		
		s = m.addItem( "R", 6, rotR,-180.0, 180, 361, null );
		s.changeVLabelText( Float.toString(rotR, "   %1.0f degrees") );
		
		m.addItem( "Reset", 0 );
	}

	public void endTuningSession( int cancelled )
	{
		if( cancelled )
		{
			posX = old_posX;
			posY = old_posY;
			posZ = old_posZ;
			rotY = old_rotY;
			rotP = old_rotP;
			rotR = old_rotR;
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}
		else
		{
			getCar_LocalVersion();
			if (the_car)
				the_car.forceUpdate();
		}
	}

	public void handleMessage( Event m )
	{
		if( m.cmd == 1 )
		{
			posX = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(posX, "   %1.3f") );
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}
		
		if( m.cmd == 2 )
		{
			posY = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(posY, "   %1.3f") );
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}
		if( m.cmd == 3 )
		{
			posZ = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(posZ, "   %1.3f") );
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}
		if( m.cmd == 4 )
		{
			rotY = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(rotY, "   %1.0f degrees") );
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}
		if( m.cmd == 5 )
		{
			rotP = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(rotP, "   %1.0f degrees") );
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}
		if( m.cmd == 6 )
		{
			rotR = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(rotR, "   %1.0f degrees") );
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}
		
		if( m.cmd == 0 )
		{
			posX = 0.0;
			posY = 0.0;
			posZ = 0.0;
			rotY = 0.0;
			rotP = 0.0;
			rotR = 0.0;
			m.gadget.osd.findGadget( this, 1 ).setValue( 0.0 );
			m.gadget.osd.findGadget( this, 1 ).changeVLabelText( Float.toString(posX, "   %1.3f") );
			
			m.gadget.osd.findGadget( this, 2 ).setValue( 0.0 );
			m.gadget.osd.findGadget( this, 2 ).changeVLabelText( Float.toString(posY, "   %1.3f") );
			
			m.gadget.osd.findGadget( this, 3 ).setValue( 0.0 );
			m.gadget.osd.findGadget( this, 3 ).changeVLabelText( Float.toString(posZ, "   %1.3f") );
			
			m.gadget.osd.findGadget( this, 4 ).setValue( 0.0 );
			m.gadget.osd.findGadget( this, 4 ).changeVLabelText( Float.toString(rotY, "   %1.0f degrees") );
			
			m.gadget.osd.findGadget( this, 5 ).setValue( 0.0 );
			m.gadget.osd.findGadget( this, 5 ).changeVLabelText( Float.toString(rotP, "   %1.0f degrees") );
			
			m.gadget.osd.findGadget( this, 6 ).setValue( 0.0 );
			m.gadget.osd.findGadget( this, 6 ).changeVLabelText( Float.toString(rotR, "   %1.0f degrees") );
			
			the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		}

	}
	//---------tuning
	
	public void updatevariables()
	{
		getCar_LocalVersion();
		the_car.setSlotPos( 401, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		super.updatevariables();
	}
	
	public float deg2rad( float deg )
	{
		return(deg * 3.141 / 180);
	}
	
	
	
}
