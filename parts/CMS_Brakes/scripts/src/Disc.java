package java.game.parts.CMS_Brakes;

import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.*;
import java.game.parts.rgearpart.reciprocatingrgearpart.brake.*;

public class Disc extends DiscBrake
{
	float c_angle, default_angle, old_angle, c_angle1, d_angle;
	float x, y, cosinus, cosinus1, sinus, sinus1, alpha, a1;
	int a2, p, p1, s;
	
	public Disc( int id )
	{
		super( id );
		name = "Universal Caliper";
		description = "Paintable";
		brand_new_prestige_value = 42.49;
		value = tHUF2USD(35.579);
		default_angle = 0.0;
		c_angle = default_angle;	
		
	}
	
	public void load( File saveGame )
	{
		super.load( saveGame );

		int	save_ver = saveGame.readInt();

		if (save_ver >= 1)
		{
			c_angle = saveGame.readFloat();
			x = saveGame.readFloat();
			y = saveGame.readFloat();
			d_angle = (c_angle/180)*3.142;
			setSlotPos( 2, new Vector3(0.0, y, x), new Ypr(0.0, -d_angle, 0) );
		}
	}
	
	public void save( File saveGame )
	{
		super.save( saveGame );

		int	save_ver = 2;

		saveGame.write( save_ver );
		if (save_ver >= 1)
		{
			saveGame.write( c_angle );
			saveGame.write( x );
			saveGame.write( y );
		}
	}
	
	public int isTuneable()
	{
		return true;
	}
	
	public void buildTuningMenu( Menu m )
	{
		old_angle = c_angle;

		m.addItem( "Caliper position", 1, c_angle, -180.0, 180, 1.1, null ).printValue("   %1.0f" + " degrees");
		
		m.addItem( "Reset to factory defaults",			0);	//this should always be with cmd=0
		
		GameLogic.player.car.wakeUp();
	}

	public void endTuningSession( int cancelled )
	{
		if( cancelled )
		{
			c_angle = old_angle;
		}
		else
		{
			if (c_angle != old_angle)
				GameLogic.spendTime(5*60);
			getCar_LocalVersion();
			if (the_car)
				the_car.forceUpdate();
		}
	}

	public void handleMessage( Event m )
	{
		if( m.cmd == 0 )
		{
			c_angle = default_angle;
			m.gadget.osd.findGadget( this, 1 ).setValue( default_angle );
			x = 0.0;
			y = 0.0;
			d_angle = 0.0;
			setSlotPos( 2, new Vector3(0.0, 0, 0), new Ypr(0.0, 0, 0) );		
		}
		else
		if( m.cmd == 1 )
		{
			c_angle = ((Slider)m.gadget).value;
		d_angle = (c_angle/180)*3.142;
		alpha = d_angle;
		sinus = 0.000;
		sinus1 = 0.000;
			for (int n=0; n<=6; n++)
			{
			p = 1;
			p1 = 1;
			s = 2 * n + 1;
				for (int f = 1; f <= s; f++)
				{
				p1 = p * f;
				p = p1;
				}
			a1 = 1.000;
				for (int g = 1; g <= s; g++)
				{
				a1 *= alpha;
				}
			a2 = -1;
				for (int g1 = 0; g1 <= n; g1++)
				{
				a2 *= -1;
				}
			
			sinus = (a2 * a1)/p;
			sinus1 += sinus;
			}	
		y = (diam_mm/2000)*sinus1;
		
		alpha = d_angle;
		cosinus = 0.000;
		cosinus1 = 0.000;
		
			for (int n=0; n<=6; n++)
			{
			p = 1;
			p1 = 1;
			s = 2*n;
				for (int f = 1; f <= s; f++)
				{
				p1 = p * f;
				p = p1;
				}
			a1 = 1.000;
				for (int g = 1; g <= s; g++)
				{
				a1 *= alpha;
				}
			a2 = -1;
				for (int g1 = 0; g1 <= n; g1++)
				{
				a2 *= -1;
				}
			
			cosinus = (a2 * a1)/p;
			cosinus1 += cosinus;
			}	
		x = ((diam_mm/2000)*(cosinus1) - (diam_mm/2000));
		
			((Slider)m.gadget).changeVLabelText( Float.toString(c_angle, "   %1.0f" + " degrees"));
			setSlotPos( 2, new Vector3(0.0, y, x), new Ypr(0.0, -d_angle, 0) );				
		}	
	}
	
	public void updatevariables()
	{
		super.updatevariables();
		int whlID = getWheelID();
		switch (whlID)
		{
			case 0:
				setSlotPos( 1, new Vector3(-0.03, 0, 0), null );
				break;
			case 1:
				setSlotPos( 1, new Vector3(0.03, 0, 0), null );
				break;
			case 2:
				setSlotPos( 1, new Vector3(-0.03, 0, 0), null );
				break;
			case 3:
				setSlotPos( 1, new Vector3(0.03, 0, 0), null );
				break;
			default:
				setSlotPos( 1, new Vector3(0.0, 0, 0), null );
				break;
		}
		setSlotPos( 2, new Vector3(0.0, y, x), new Ypr(0.0, -d_angle, 0) );	
	}	

}