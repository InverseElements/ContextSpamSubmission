﻿using Ionic.Zip;
using Microsoft.Office.Interop.Outlook;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ContextSpamSubmission
{
    class WorkLogic
    {
        const string PR_MAIL_HEADER_TAG = @"http://schemas.microsoft.com/mapi/proptag/0x007D001E";
        const string PR_ATTACH_DATA_BIN = @"http://schemas.microsoft.com/mapi/proptag/0x37010102";
        //variable declarations XXX
        //the registry hive containing our address keys.
        string sRegPath = "HKEY_LOCAL_MACHINE\\SOFTWARE\\InverseSoftware\\SpamSubmission\\";
        //the key containing the ticket 'voicemail' address. Emailing this address should result in
        //a ticket being created, with a reference to the SPAM sample.
        string sRegTicketAddress = "ticketEmail";
        //the key containing the address we submit the SPAM sample to.
        string sRegSubmitAddress = "spamEmail";
        //key that holds the zip password
        string sRegEncryptionPassword = "encp";
        //string to store the registry key holding the Debug value
        string sRegDebug = "debug";
        //key to hold the ticket voicemail address, once we get it.
        string sEmailTicketAddress = "";
        //key to hold the SPAM submission address, once we get it.
        string sSpamSubmitAddress = "";
        //string to store encryption password
        string sEncryptionPassword = "";
        //A string to hold the interesting items we want to report on in plaintext
        string sMetadata = "";
        //boolean value (stored in reg) dictating wether or not we should show debugging messages
        bool bDebug = false;
        //an Outlook Rules Array to store all the current outlook rules.
        Rules olRuleList = null;
        //Single Rule instance
        Rule olRule = null;
        //A string for the rule name we will use. Registry?
        string olRuleName = "SPAM Auto Delete List";
        //Boolean value for existence of SPAM Rule in outlook
        bool bSpamRuleExists = false;
        
        public void submit()
        {
            //get our reference to the application for future use.
            Microsoft.Office.Interop.Outlook.Application outlookApp = Globals.ThisAddIn.Application;

            //Main logic, majority of program logic is below, in this method.
            Explorer explorer = Globals.ThisAddIn.Application.ActiveExplorer();

            //this checks something, and is probably important. Copy Paste from the interwebs.
            //Looks like it double checks that something has been right clicked and passed to the application
            if (explorer != null && explorer.Selection != null && explorer.Selection.Count > 0)
            {
                //item = variable storing what was right clicked on.
                //why we use 1 instead of 0 is unknown, but it works
                object item = explorer.Selection[1];
                //if the item selected is a mail item, we know the user has done it right, let's proceed.
                if (item is MailItem)
                {
                    //store badmail for future use
                    MailItem badMail = item as MailItem;
                    if (badMail != null)
                    {
                        //set the guid of this message
                        string sGuid = Guid.NewGuid().ToString();
                        string sHeaders = "";
                        PropertyAccessor oPA = badMail.PropertyAccessor as PropertyAccessor;

                        try
                        {
                            sHeaders = (string)oPA.GetProperty(PR_MAIL_HEADER_TAG);
                        }
                        catch (System.Exception e)
                        {
                            if (bDebug)
                            {
                                MessageBox.Show("Exception getting mail headers: \n\n" + e,
                        "Error", MessageBoxButtons.OK);
                            }
                        }

                        //This will pull out the headers and such, and whacks them into a string.
                        try
                        {
                            sMetadata = "To: " + badMail.To + "\r\n";
                            sMetadata += "From: " + badMail.SenderName + ": " + badMail.SenderEmailAddress + "\r\n";
                            sMetadata += "Subject: " + badMail.Subject + "\r\n";
                            sMetadata += "CC: " + badMail.CC + "\n\r";
                            sMetadata += "Companies Associated With Email: " + badMail.Companies + "\r\n";
                            sMetadata += "Email Creation Time: " + badMail.CreationTime + "\r\n";
                            sMetadata += "Delivery Report Requested: " + badMail.OriginatorDeliveryReportRequested + "\r\n";
                            sMetadata += "Received Time: " + badMail.ReceivedTime + "\r\n";
                            sMetadata += "Sent On: " + badMail.SentOn.ToString() + "\r\n";
                            sMetadata += "Size (kb): " + ((badMail.Size) / 1024).ToString() + "\r\n";
                            sMetadata += "Headers: \r\n" + sHeaders + "\r\n";
                            sMetadata += "Plaintext Body: \r\n" + badMail.Body + "\r\n";
                        }
                        catch(System.Exception e)
                        {
                            MessageBox.Show("Exception extracting metadata: \n\n" + e,
                        "Error", MessageBoxButtons.OK);
                        }


                        //This will create a mail item, and send it to the designated mailbox of a ticketing system.
                        try
                        {
                            MailItem ticketMail = (MailItem)outlookApp.CreateItem(OlItemType.olMailItem);
                            ticketMail.To = sEmailTicketAddress;
                            ticketMail.Subject = sGuid;
                            ticketMail.Body = sMetadata;
                            ticketMail.Send();
                        }
                        catch(System.Exception e)
                        {
                            MessageBox.Show("Exception sending ticket email: \n\n" + e,
                        "Error", MessageBoxButtons.OK);
                        }

                        /*
                        *   Save the badmail to disk, to then read back in in a compressed stream.
                        *   Then, email it to the submission email address, with the bad mail attached.
                        */
                        try
                        {
                            //First, get temp path(checks the below in order):
                            //The path specified by the TMP environment variable.
                            //The path specified by the TEMP environment variable.
                            //The path specified by the USERPROFILE environment variable.
                            //The Windows directory.
                            string tempDir = Path.GetTempPath();
                            string badOnDisk = tempDir + sGuid + ".msg";
                            string badZipOnDisk = tempDir + sGuid + ".zip";

                            //Save it to disk
                            badMail.SaveAs(badOnDisk);

                            //Read in the email in the .zip format, with a password, write back to disk.
                            using (ZipFile zip = new ZipFile())
                            {
                                zip.Password = sEncryptionPassword;
                                //the "." specifies the directory structure inside the zip - . just means
                                //insert the attachment at the root, instead of nested in a replication of
                                //the systems temp dir
                                zip.AddFile(badOnDisk, ".");
                                zip.Save(badZipOnDisk);
                            }

                            //Better get rid of the raw BadMail as soon as we're done with it
                            File.Delete(badOnDisk);

                            //This will create a mail item, and send it to a sample collection mailbox, with the badSample attached.
                            MailItem spamMail = (MailItem)outlookApp.CreateItem(OlItemType.olMailItem);
                            spamMail.To = sSpamSubmitAddress;
                            spamMail.Subject = sGuid;
                            spamMail.Body = sMetadata;
                            spamMail.Attachments.Add(badZipOnDisk, OlAttachmentType.olByValue, 1, "SPAM Sample " + sGuid);
                            spamMail.Send();

                            //That's sent, let's delete the .zip on disk
                            File.Delete(badZipOnDisk);
                        }
                        catch(System.Exception e)
                        {
                            if (bDebug)
                            {
                                MessageBox.Show("Exception somewhere in the emailing of the SPAM submission.\n" +
                                " Most likely cause here is that AV picked up the bad mail on disk.\n" +
                                "Exception:\n\n" + e,
                                "Error", MessageBoxButtons.OK);
                            }
                        }
                        
                        

                        //if the listed email address doesn't contain an @, it's not a legit threat address, disregard blocking.
                        if (badMail.SenderEmailAddress.Contains("@"))
                        {
                            DialogResult blockSender = MessageBox.Show("Do you want to automatically delete future emails from:\n" + badMail.SenderEmailAddress, 
                                "Block Sender?", MessageBoxButtons.YesNo);
                            if (blockSender == DialogResult.Yes)
                            {
                                blacklistSender(badMail.SenderEmailAddress);
                            }
                        }

                        //Finally, remove the dodgy email from outlook.
                        try
                        {
                            badMail.UnRead = false;
                            badMail.Save();
                            badMail.Delete();
                        } catch (System.Exception e)
                        {
                            MessageBox.Show("Exception deleting SPAM email: \n\n" + e,
                        "Error", MessageBoxButtons.OK);
                        }


                        //If we're debugging, let's show the success and contents.
                        if (bDebug)
                        {
                            MessageBox.Show("You've submitted a SPAM sample.\r\n" +
                                sMetadata, "Thanks", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    
                    //Not a mail item, need to decide how to handle this. Advise user they done goofed.
                    //Should never happen, theoretically.
                    else
                    {
                        MessageBox.Show("You've selected something that is not an email.\r\n" + 
                            "Please ensure you right click the email you want to submit, and try again.\r\n\r\n" +
                            "If the issue persists, please contact the service centre for assistance.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                }
            }
        }
       
        public bool initialize()
        {
            //bDebug string for testing
            string sDebugMessage= "";
            bool bInitialized = false;
            //let's grab our stuff from the registry
            try
            {
                //bDebug = Registry.LocalMachine.OpenSubKey(sRegPath).GetValue(sRegDebug).ToString().ToLower().Equals("true");
                bDebug = Registry.GetValue(sRegPath, sRegDebug, "false").ToString().ToLower().Equals("true");
                sDebugMessage += "Got Debug Value";
                sEmailTicketAddress = Registry.GetValue(sRegPath, sRegTicketAddress, "").ToString();
                sDebugMessage += "Email Ticket Address: " + sEmailTicketAddress + "\n";

                sSpamSubmitAddress = Registry.GetValue(sRegPath, sRegSubmitAddress, "").ToString();
                sDebugMessage += "Spam Submit Address: " + sSpamSubmitAddress + "\n";

                sEncryptionPassword = Registry.GetValue(sRegPath, sRegEncryptionPassword, "").ToString();
                sDebugMessage += "Encryption Password: " + sEncryptionPassword + "\n";

                bInitialized = true;
            }
            catch (System.Exception e)
            {
                MessageBox.Show("The SPAM Submission plug in has failed to load.\n" +
                    "Please contact the Service Desk and tell them your reg keys need re-configuring.\n",
                    "Error",MessageBoxButtons.OK, MessageBoxIcon.Error);
                bInitialized = false;
            }
            if (bDebug)
            {
                MessageBox.Show(sDebugMessage, "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return bInitialized;
        }

        private void blacklistSender(string sSenderEmailAddress)
        {
            //Now, let's add the sender to the autodelete rule
            try
            {
                olRuleList = Globals.ThisAddIn.Application.Session.DefaultStore.GetRules();

                foreach (Rule rule in olRuleList)
                {
                    if (rule.Name.Equals(olRuleName))
                    {
                        olRule = rule;
                        bSpamRuleExists = true;
                        break;
                    }
                }
            }
            catch (System.Exception e)
            {
                if (bDebug)
                {
                    MessageBox.Show("Exception getting existing Outlook Rules: \n\n" + e,
                        "Error", MessageBoxButtons.OK);
                }
            }
            
            try
            {
                if (!bSpamRuleExists)
                {
                    //confirm that it doesn't exist and this isn't just a first run setting
                    //if the rule still doesn't exist, we create it. 
                    if (!bSpamRuleExists)
                    {
                        olRule = olRuleList.Create(olRuleName, OlRuleType.olRuleReceive);
                        olRule.Conditions.SenderAddress.Address = new string[] { "1@2.3" };
                    }
                }
            }
            catch (System.Exception e)
            {
                if (bDebug)
                {
                    MessageBox.Show("Exception creating SPAM rule: \n\n" + e,
                        "Error", MessageBoxButtons.OK);
                }
            }
            

            //then we check to see if the sender is in the bad list. If he's not,
            //we add him.
            bool bSenderInList = false;
            try
            {
                foreach (string s in olRule.Conditions.SenderAddress.Address)
                {
                    if (s.Equals(sSenderEmailAddress))
                    {
                        bSenderInList = true;
                        break;
                    }
                }
            }
            catch (System.Exception e)
            {
                if (bDebug)
                {
                    MessageBox.Show("Exception iterating through current rules: \n\n" + e,
                        "Error", MessageBoxButtons.OK);
                }
            }

            if (!bSenderInList)
            {
                try
                {
                    string[] saBadAddresses = new string[olRule.Conditions.SenderAddress.Address.Length + 1];
                    int i = 0;
                    foreach (string s in olRule.Conditions.SenderAddress.Address)
                    {
                        saBadAddresses[i] = s;
                        i++;
                    }
                    saBadAddresses[i] = sSenderEmailAddress;
                    olRule.Conditions.SenderAddress.Address = saBadAddresses;
                    olRule.Conditions.SenderAddress.Enabled = true;
                } 
                catch (System.Exception e)
                {
                    if (bDebug)
                    {
                        MessageBox.Show("Exception adding the address to the blacklist:\n\n" + e,
                            "Error", MessageBoxButtons.OK);
                    }
                }
            }

            try
            {
                olRule.Actions.DeletePermanently.Enabled = true;
                olRuleList.Save(true);
            }
            catch (System.Exception e)
            {
                if (bDebug)
                {
                    MessageBox.Show("Exception setting permanent delete or saving the revised blacklist:\n\n" + e,
                        "Error", MessageBoxButtons.OK);
                }
            }
        }
    }
}
