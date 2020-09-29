package java.game.parts.bodypart;

import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.render.osd.*;
import java.game.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;
import java.game.parts.rgearpart.reciprocatingrgearpart.*;

public class Wing extends BodyPart
{
	float posX, posY, posZ, rotY, rotP, rotR, sc;
	Part parent;
	int parentSlotID;

	public Wing( int id )
	{
		super( id );
        sc = 1.0;
		name = "Wing";

		prestige_calc_weight = 15.0;
		drag_reduction = 0.06;
	}
	
	public void load( File saveGame,int version )
	{
		super.load( saveGame );
		{
			posX	= saveGame.readFloat();
			posY	= saveGame.readFloat();
			posZ	= saveGame.readFloat();
			rotY	= saveGame.readFloat();
			rotP	= saveGame.readFloat();
			rotR	= saveGame.readFloat();
			sc      = saveGame.readFloat();
		}
	}

	public void save( File saveGame,int fileVersion )
	{
		super.save( saveGame );

		saveGame.write( posX );
		saveGame.write( posY );
		saveGame.write( posZ );
		saveGame.write( rotY );
		saveGame.write( rotP );
		saveGame.write( rotR );
		saveGame.write( sc );
	}
	
	public int isTuneable()
	{
		return 1;
	}
	
	
	float old_posX, old_posY, old_posZ, old_rotY, old_rotP, old_rotR, old_sc;
	ResourceRef temp_meshRes = null;
	public void buildTuningMenu( Menu m )
	{
		Slider s;
		
		old_posX = posX;
		old_posY = posY;
		old_posZ = posZ;
		old_rotY = rotY;
		old_rotP = rotP;
		old_rotR = rotR;
		old_sc = sc;
		
		s = m.addItem( "X", 1, posX, -5.0, 5.0, 801, null );
		s.changeVLabelText( Float.toString(posX, "   %1.3f") );
		
		s = m.addItem( "Y", 2, posY, -5.0, 5.0, 801, null );
		s.changeVLabelText( Float.toString(posY, "   %1.3f") );
		
		s = m.addItem( "Z", 3, posZ, -5.0, 5.0, 801, null );
		s.changeVLabelText( Float.toString(posZ, "   %1.3f") );
		
		s = m.addItem( "Y", 4, rotY, -180.0, 180, 361, null );
		s.changeVLabelText( Float.toString(rotY, "   %1.0f degrees") );
		
		s = m.addItem( "P", 5, rotP, -180.0, 180, 361, null );
		s.changeVLabelText( Float.toString(rotP, "   %1.0f degrees") );
		
		s = m.addItem( "R", 6, rotR,-180.0, 180, 361, null );
		s.changeVLabelText( Float.toString(rotR, "   %1.0f degrees") );
		
		s = m.addItem( "Scale", 9, sc,0.01, 3, 299, null );
		s.changeVLabelText( Float.toString(sc, "   %1.3f") );
		
		//Button b = null;
		//b = m.addItem( "Scale Up", 7 );
		//b = m.addItem( "Scale Down", 8 );
		
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
			if(temp_meshRes != null)
				temp_meshRes.scaleMesh(old_sc/sc,old_sc/sc,old_sc/sc);
			sc = old_sc;
		}
		else
		{
			getCar_LocalVersion();
			if (the_car)
				the_car.forceUpdate();
		}
		parent.setSlotPos( parentSlotID, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
	}

	public void handleMessage( Event m )
	{
		if(temp_meshRes == null)
		{
			int meshID = getMesh();
			temp_meshRes = new ResourceRef(meshID);
			temp_meshRes.duplicate(temp_meshRes);
			setMesh(temp_meshRes.id());
		}
		if( m.cmd == 1 )
		{
			posX = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(posX, "   %1.3f") );
		}
		
		if( m.cmd == 2 )
		{
			posY = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(posY, "   %1.3f") );
		}
		if( m.cmd == 3 )
		{
			posZ = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(posZ, "   %1.3f") );
		}
		if( m.cmd == 4 )
		{
			rotY = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(rotY, "   %1.0f degrees") );
		}
		if( m.cmd == 5 )
		{
			rotP = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(rotP, "   %1.0f degrees") );
		}
		if( m.cmd == 6 )
		{
			rotR = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(rotR, "   %1.0f degrees") );
		}
		if( m.cmd == 9 )
		{
			float newSc = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(sc, "   %1.3f") );
			temp_meshRes.scaleMesh(newSc/sc,newSc/sc,newSc/sc);
			sc = newSc;
		}
		if( m.cmd == 7 )
		{
			sc *= 1.01;
			temp_meshRes.scaleMesh(1.01,1.01,1.01);
		}
		if( m.cmd == 8 )
		{
			sc *= 0.99;
			temp_meshRes.scaleMesh(0.99,0.99,0.99);
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
			
			m.gadget.osd.findGadget( this, 9 ).setValue( 1.0 );
			m.gadget.osd.findGadget( this, 9 ).changeVLabelText( Float.toString(sc, "   %1.3f") );
			
			temp_meshRes.scaleMesh(1.0/sc,1.0/sc,1.0/sc);
			sc = 1.0;
		}
		
		parent.setSlotPos( parentSlotID, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
	}
	//---------tuning
	
	public Part getparent()
	{
		return partOnSlot(getSlotID(-1));
	}

	public int getparentSlotID()
	{
		int slotIndex;
		int slotID;
		int slots = getparent().getSlots();
		int parentSlotID = getparent().getSlotID( -1 );
		
		for( slotIndex=0; slotIndex<slots; slotIndex++ )
		{
			slotID = getparent().getSlotID( slotIndex );
			if( slotID != parentSlotID )
			{
				Part p;
				p = getparent().partOnSlot(slotID);
				if( p == this )
				{
					return slotID;
				}
			}
		}
		return 0;
	}
	
	public void install()
	{
		super.install();
		parent = getparent();
		parentSlotID = getparentSlotID();
		parent.setSlotPos( parentSlotID, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
	}
	
	public void repair()
	{
		super.repair();
		Thread SetPosAgain = new SetAgainWing( posX, posY, posZ, rotY, rotP, rotR, parent, parentSlotID );
		SetPosAgain.start();
		int meshID = getMesh();
		temp_meshRes = new ResourceRef(meshID);
		temp_meshRes.duplicate(temp_meshRes);
		setMesh(temp_meshRes.id());
		temp_meshRes.scaleMesh(sc,sc,sc);
	}
	
	public float deg2rad( float deg )
	{
		return(deg * 3.141 / 180);
	}
}

public class SetAgainWing extends Thread
{
	float posX, posY, posZ, rotY, rotP, rotR;
	Part parent;
	int parentSlotID;

	public SetAgainWing( float posX2, float posY2, float posZ2, float rotY2, float rotP2, float rotR2, Part parent2, int pID )
	{
		posX = posX2;
		posY = posY2;
		posZ = posZ2;
		rotY = rotY2;
		rotP = rotP2;
		rotR = rotR2;
		
		parent = parent2;
		parentSlotID = pID;
	}

	public void run()
	{
		parent.setSlotPos( parentSlotID, new Vector3(posX,posZ,posY), new Ypr(deg2rad(rotY),deg2rad(rotP),deg2rad(rotR)) );
		if(parent instanceof Wing)
		{
			Wing parentWing = parent;
		}
		super.stop();
	}
	
	public float deg2rad( float deg )
	{
		return(deg * 3.141 / 180);
	}
}
