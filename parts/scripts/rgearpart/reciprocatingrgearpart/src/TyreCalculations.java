package java.game.parts.rgearpart.reciprocatingrgearpart;
import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.cars.*;
import java.game.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;
/*
Code copied from :
http://bndtechsource.ucoz.com/index/tire_data_calculator/0-20
Converted to Java and edited by mindeliszz VStanced.com
*/
public class TyreCalculations //implements Tyre
{
	float tyreWidth=195;
	float aspectRatio=55;
	float rimWidth=6;
	float airPressure=1;
	float weightOnWheel=150;
	
	int index=102;
	
	float vscf=3.1416;
	
	float vsecwth;//sidewall width
	float vsecht;//sidewall height
	float vcpw;//contact patch width
	float vstifrate;//stiffness rate
	float cpa;//contact patch area
	
	// ff = steady state circumference factor (about 97% pi) at 60 km/h (from mfr data)//
	float ff = 3.05;
	
	//fr = sidewall deflection factor at max load at specified air pressure (from mfr data)//
	float fr = 0.776;
	
	public void setVars(float tWidth,float aRatio, float rWidth,float aPressure,float wOnWheel)
	{
	tyreWidth=tWidth;
	aspectRatio=aRatio;
	rimWidth=rWidth;
	airPressure=aPressure;
	weightOnWheel=wOnWheel;
	
	float loadIndex;
	switch(index){
		case 62: loadIndex=265; break;
		case 63: loadIndex=272; break;
		case 64: loadIndex=280; break;
		case 65: loadIndex=290; break;
		case 66: loadIndex=300; break;
		case 67: loadIndex=307; break;
		case 68: loadIndex=315; break;
		case 69: loadIndex=325; break;
		case 70: loadIndex=335; break;
		case 71: loadIndex=345; break;
		case 72: loadIndex=355; break;
		case 73: loadIndex=365; break;
		case 74: loadIndex=375; break;
		case 75: loadIndex=387; break;
		case 76: loadIndex=400; break;
		case 77: loadIndex=412; break;
		case 78: loadIndex=425; break;
		case 79: loadIndex=437; break;
		case 80: loadIndex=450; break;
		case 81: loadIndex=462; break;
		case 82: loadIndex=475; break;
		case 83: loadIndex=487; break;
		case 84: loadIndex=500; break;
		case 85: loadIndex=515; break;
		case 86: loadIndex=530; break;
		case 87: loadIndex=545; break;
		case 88: loadIndex=560; break;
		case 89: loadIndex=580; break;
		case 90: loadIndex=600; break;
		case 91: loadIndex=615; break;
		case 92: loadIndex=630; break;
		case 93: loadIndex=650; break;
		case 94: loadIndex=670; break;
		case 95: loadIndex=690; break;
		case 96: loadIndex=710; break;
		case 97: loadIndex=730; break;
		case 98: loadIndex=750; break;
		case 99: loadIndex=775; break;
		case 100: loadIndex=800; break;
		case 101: loadIndex=825; break;
		case 102: loadIndex=850; break;
		case 103: loadIndex=875; break;
		case 104: loadIndex=900; break;
		case 105: loadIndex=925; break;
		case 106: loadIndex=950; break;
		case 107: loadIndex=975; break;
		case 108: loadIndex=1000; break;
		case 109: loadIndex=1030; break;
		case 110: loadIndex=1060; break;
		case 111: loadIndex=1090; break;
		case 112: loadIndex=1120; break;
		case 113: loadIndex=1150; break;
		case 114: loadIndex=1180; break;
		case 115: loadIndex=1215; break;
		default: loadIndex=1215;
		}
	
	//drim = manufacturers specified design rim width for different aspect ratios//
	float drim;
	if(aspectRatio>=80) {drim = tyreWidth*0.7/25.4;}
	else if(aspectRatio<=79 && aspectRatio>=60) {drim = tyreWidth*0.75/25.4;}
	else if(aspectRatio<=59 && aspectRatio>=50) {drim = tyreWidth*0.8/25.4;}
	else if(aspectRatio<=49 && aspectRatio>=45) {drim = tyreWidth*0.85/25.4;}
	else if(aspectRatio<=44 && aspectRatio>=30) {drim = tyreWidth*0.9/25.4;}
	else {drim=tyreWidth*0.925/25.4;}
	
	//dr = rim dia converted to metric//
	float dr = rimWidth * 25.4;
	
	//h = sidewall height based on tire section width and aspect ratio//
	float h = tyreWidth * aspectRatio / 100;
	
	//drimck = check the input rim width against the manufacturers design rim width//
	/*float drimck;
	if ( rimWidth!= drim )
	{ drimck = drim - rimWidth; }
	else 
	{drimck = 0;}*/
	
	//secwth = the section width changes 5mm for every .5” change in rim width//
	/*float secwth;
	if (drimck == 0)
	{secwth = tyreWidth;}
	else if (drimck == -1)
	{secwth = tyreWidth+10;}
	else if (drimck == -0.5)
	{secwth = tyreWidth+5;}
	else if (drimck == 0.5)
	{secwth = tyreWidth-5;}
	else if (drimck == 1)
	{secwth = tyreWidth-10;}
	else 
	{secwth =(0);}*/
	
	//secht = the section height changes 2.5mm for every .5” change in rim width//
	/*float secht;
	if (drimck == 0)
	{secht = h;}
	else if (drimck == -1)
	{secht = h-5;}
	else if (drimck == -0.5)
	{secht = h-2.5;}
	else if (drimck == 0.5)
	{secht = h+2.5;}
	else if (drimck == 1)
	{secht = h+5;}
	else 
	{secht =(0);}*/
	
	//cr = steady state rolling circumference at 30-50% max load, 60km/h and mfr spec air pressure//
	float cr = ff * ((2 * h) + dr);
	
	//rs = static radius at max load and mfr spec air pressure//
	float rs = (0.5 * dr) + (fr * h);
	
	//rd = steady state dynamic rolling radius at 30-50% max load, 60km/h and mfr spec air pressure//
	float rd = cr / 3.1416 / 2;
	
	//d = outer dia with no load//
	float d = (2 * h) + dr;
	
	//stifrate = spec. tire stiffness rate at max load and spec air pressure //
	float stifrate = (loadIndex)/ ((d/2) - rs);
	
	//vstifrate = vehicle tire stiffness rate at veh speed and veh air pressure //
	vstifrate = (((((vscf*d)/3.1416)/2)/(d/2))*(airPressure/2.5))*stifrate;
	
	//crnrld= vehicle corner load input by user //
	float crnrld = weightOnWheel;
	
	//vdfl = spec. tire stiffness rate at max load and spec air pressure //
	float vdfl = crnrld/vstifrate;
	
	//vsrd = vehicle speed dynamic rolling radius at veh speed, veh load and veh air pressure //
	float vsrd = (d/2)-vdfl;
	
	//vscr = vehicle speed rolling circumference at veh speed, veh load and veh air pressure //
	//float vscr = vsrd*2*3.1416;
	
	//v = volume of air inside tire in liters (10 = sidewall thickness and 20.5 = tread thickness)//
	//float v = (tyreWidth - 10) * (h -20.5) * (dr + (h -20.5)) * 3.1416 / 1000000;
	
	//vsecht = vehicle section height at veh speed, veh load and veh air pressure //
	vsecht = (d/2)-(dr/2)-vdfl;
	
	//statcpw = contact patch static width// 
	float statcpw = (1.075-(0.005*aspectRatio))* (tyreWidth*1.001);
	
	//vcpw = vehicle contact patch width at veh load and veh air pressure // 
	vcpw = statcpw*(((-0.019355*airPressure)+0.04)+1)* (((0.000000079*(crnrld*crnrld))+(0.000074023*crnrld))+1);
	
	//chordr = for use in vehicle section width //
	float chordr = Math.sqrt((((tyreWidth-statcpw)/2)*((tyreWidth-statcpw)/2))+((h/2)*(h/2)));
	
	//vsecwth = vehicle section width at veh speed, veh load and veh air pressure //
	vsecwth = (Math.sqrt ((chordr*chordr)-((vsecht/2)*(vsecht/2)))*2)+vcpw;
	
	//statcpl = contact patch length//
	float statcpl = Math.sqrt((d/2)*(d/2)-(vsrd*vsrd))*2;
	
	//vcpl = vehicle contact patch length at veh load and veh air pressure // 
	float vcpl = statcpl*(((0.009874*(airPressure*airPressure))-(0.117384*airPressure))+1.32)* (((0.000000035*(crnrld*crnrld))+(0.000172792*crnrld))+0.49);
	
	//cpla = contact patch area//
	cpa = (vcpl*vcpw)/100;
	}
	
	public float getWeightOnWheel( int i )
	{
		float lenght=0.0;
		float fromCenter=0.0;
		float val=0;
		Vector3[] WP = new Vector3[16];
		if(GameLogic.player.car)
		{
			float carMass = GameLogic.player.car.chassis.getMass();
			Vector3 centerMass = GameLogic.player.car.chassis.getCM();
			int wheels = GameLogic.player.car.chassis.getWheels();
			for(int j=0; j<wheels; j++)
			{
				WheelRef wheel = GameLogic.player.car.chassis.getWheel(j);
				WP[j] = wheel.getPos();
				if((wheels-1)-j==i) {
				fromCenter = Math.sqrt((WP[j].z-centerMass.z)*(WP[j].z-centerMass.z)+(WP[j].x-centerMass.x)*(WP[j].x-centerMass.x));
				}
				lenght += Math.sqrt((WP[j].z-centerMass.z)*(WP[j].z-centerMass.z)+(WP[j].x-centerMass.x)*(WP[j].x-centerMass.x));
			}
			val = fromCenter/lenght*carMass;
		}
		return val;
	}
	
	public float getTyreHeight()
	{ return vsecht; }
	
	public float getTyreWidth()
	{ return vsecwth; }
	
	public float getContactPatchWidth()
	{ return vcpw; }
	
	public float getContactPatchArea()
	{ return cpa; }
	
	public float getStiffnessRate()
	{ return vstifrate; }
	
	public float sqrt(float var, int mul)
	{
		for(int i=0; i<mul; i++)
			var=Math.sqrt(var);
		return var;
	}
}